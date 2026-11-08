using System.Text;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Domain;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;

namespace PropFlow.Modules.AiChatbot.Infrastructure;

public sealed class KnowledgeDocuments(AiChatbotDbContext db, IChatAi? ai = null) : IKnowledgeDocuments, IPublishedKnowledgeSource
{
    private sealed class VisibleRevision
    {
        public KnowledgeDocument Document { get; init; } = null!;
        public KnowledgeRevision Revision { get; init; } = null!;
    }
    private IQueryable<VisibleRevision> Visible(bool manage) =>
        from d in db.Documents.AsNoTracking()
        join r in db.Revisions.AsNoTracking() on new { Id = d.Id, Number = manage ? d.LatestRevision : d.PublishedRevision ?? 0 }
            equals new { Id = r.DocumentId, r.Number }
        select new VisibleRevision { Document = d, Revision = r };

    private static KnowledgeDocumentSummary Summary(VisibleRevision row) => new(row.Document.Id, row.Revision.Title,
        row.Revision.Audience, row.Revision.Number, row.Document.PublishedRevision, row.Document.Token, row.Document.UpdatedAt);

    public async Task<KnowledgeDocumentPage> ListAsync(bool manage, string? search, int page, CancellationToken ct)
    {
        if (page < 1 || page > 100000 || search?.Length > 200) throw new KnowledgeValidationException("Thông tin tìm kiếm không hợp lệ.");
        var query = Visible(manage);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
            query = query.Where(x => x.Revision.Title.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Document.UpdatedAt).ThenBy(x => x.Document.Id).Skip((page - 1) * 12).Take(12)
            .Select(x => new KnowledgeDocumentSummary(x.Document.Id, x.Revision.Title, x.Revision.Audience, x.Revision.Number,
                x.Document.PublishedRevision, x.Document.Token, x.Document.UpdatedAt)).ToArrayAsync(ct);
        return new(items, total, page, 12);
    }

    public async Task<KnowledgeDocumentDetail?> GetAsync(Guid id, bool manage, CancellationToken ct)
    {
        var row = await Visible(manage).SingleOrDefaultAsync(x => x.Document.Id == id, ct);
        if (row is null) return null;
        var summary = Summary(row);
        var revisions = db.Revisions.AsNoTracking().Where(x => x.DocumentId == id);
        // Staff never receives draft text or draft history, even when guessing an ID.
        if (!manage) revisions = revisions.Where(x => x.Number == summary.Revision);
        else revisions = revisions.Where(x => x.Number > summary.Revision - 20 || x.Number == summary.PublishedRevision);
        var history = await revisions.OrderByDescending(x => x.Number)
            .Select(x => new KnowledgeRevisionView(x.Number, x.Title, x.Content, x.Audience, x.CreatedAt)).ToArrayAsync(ct);
        var activity = manage ? await db.Activities.AsNoTracking().Where(x => x.DocumentId == id)
            .OrderByDescending(x => x.CreatedAt).Select(x => new KnowledgeAuditView(x.Action, x.Revision, x.CreatedAt)).Take(50).ToArrayAsync(ct) : [];
        return new(summary, history.Single(x => x.Number == summary.Revision), history, activity);
    }

    public async Task<Guid> SaveAsync(Guid? id, SaveKnowledgeDocument request, Guid actor, CancellationToken ct)
    {
        var document = id is null ? KnowledgeDocument.Create() : await FindForWrite(id.Value, request.ExpectedToken, ct);
        var revision = document.Save(request.Title, request.Content, request.Audience, actor, DateTimeOffset.UtcNow);
        if (id is null) db.Documents.Add(document);
        db.Revisions.Add(revision);
        db.Activities.Add(new(document.Id, revision.Number, id is null ? "CREATE" : "EDIT", actor, document.UpdatedAt));
        await Commit(ct);
        return document.Id;
    }

    public async Task SetPublicationAsync(Guid id, Guid expectedToken, bool publish, Guid actor, CancellationToken ct)
    {
        var document = await FindForWrite(id, expectedToken, ct);
        var number = document.PublishedRevision ?? document.LatestRevision;
        if (publish)
        {
            document.Publish(DateTimeOffset.UtcNow); number = document.LatestRevision;
            var revision = await db.Revisions.SingleAsync(x => x.DocumentId == id && x.Number == number, ct);
            if (revision.Audience == "RESIDENT" && !await db.Chunks.AnyAsync(x => x.DocumentId == id && x.Revision == number, ct))
                db.Chunks.AddRange(KnowledgeText.Split(id, number, revision.Title, revision.Content));
        }
        else document.Unpublish(DateTimeOffset.UtcNow);
        db.Activities.Add(new(document.Id, number, publish ? "PUBLISH" : "UNPUBLISH", actor, document.UpdatedAt));
        await Commit(ct);
    }

    public Task<PublishedKnowledgeSource?> FindResidentSourceAsync(Guid documentId, CancellationToken ct) =>
        (from d in db.Documents.AsNoTracking()
         join r in db.Revisions.AsNoTracking() on new { Id = d.Id, Number = d.PublishedRevision ?? 0 }
             equals new { Id = r.DocumentId, r.Number }
         where d.Id == documentId && r.Audience == "RESIDENT"
         select new PublishedKnowledgeSource(d.Id, r.Number, r.Title, r.Content)).SingleOrDefaultAsync(ct);

    public async Task<KnowledgeIndexStatus?> IndexStatusAsync(Guid id, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (document is null) return null;
        var chunks = db.Chunks.AsNoTracking().Where(x => x.DocumentId == id && x.Revision == document.PublishedRevision);
        var model = ai?.EmbeddingKey;
        return new(ai?.Ready == true, await chunks.CountAsync(ct),
            model is null ? 0 : await chunks.CountAsync(x => x.Embedding != null && x.EmbeddingModel == model, ct));
    }

    private async Task<KnowledgeDocument> FindForWrite(Guid id, Guid? token, CancellationToken ct)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KnowledgeNotFoundException();
        if (token is null || document.Token != token) throw new KnowledgeConflictException();
        return document;
    }
    private async Task Commit(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new KnowledgeConflictException(); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new KnowledgeConflictException(); }
    }
}
