using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Domain;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;

namespace PropFlow.Modules.AiChatbot.Infrastructure;

public sealed class KnowledgeIndex(AiChatbotDbContext db, IChatAi ai)
{
    public async Task TickAsync(CancellationToken ct)
    {
        // Backfill published documents that existed before chunk storage was introduced.
        var missing = await (from d in db.Documents.AsNoTracking()
                             join r in db.Revisions.AsNoTracking() on new { Id = d.Id, Number = d.PublishedRevision ?? 0 }
                                 equals new { Id = r.DocumentId, r.Number }
                             where r.Audience == "RESIDENT" && !db.Chunks.Any(c => c.DocumentId == d.Id && c.Revision == r.Number)
                             orderby d.Id
                             select r).FirstOrDefaultAsync(ct);
        if (missing is not null)
        {
            db.Chunks.AddRange(KnowledgeText.Split(missing.DocumentId, missing.Number, missing.Title, missing.Content));
            await db.SaveChangesAsync(ct);
        }
        if (!ai.Ready) return;
        var embeddingKey = ai.EmbeddingKey;
        var before = DateTimeOffset.UtcNow.AddHours(-1);
        var pending = await (from c in db.Chunks
                             join d in db.Documents on new { Id = c.DocumentId, c.Revision } equals new { Id = d.Id, Revision = d.PublishedRevision ?? 0 }
                             join r in db.Revisions on new { Id = c.DocumentId, Number = c.Revision } equals new { Id = r.DocumentId, r.Number }
                             where r.Audience == "RESIDENT" && (c.EmbeddingModel != embeddingKey || c.Embedding == null)
                                 && (c.EmbeddingModel != embeddingKey || c.AttemptedAt == null || c.AttemptedAt < before)
                             orderby c.DocumentId, c.Revision, c.Number
                             select new { Chunk = c, r.Title }).Take(8).ToArrayAsync(ct);
        if (pending.Length == 0) return;
        // A provider/model change gets one immediate attempt; failures then retain
        // the normal cooldown for that target rather than retrying every tick.
        foreach (var item in pending) item.Chunk.Attempt(DateTimeOffset.UtcNow, embeddingKey);
        await db.SaveChangesAsync(ct);
        float[][] vectors;
        try { vectors = await ai.EmbedAsync(pending.Select(x => KnowledgeText.Redact(x.Title + "\n" + x.Chunk.Content)).ToArray(), ct); }
        catch (ChatAiUnavailableException) { return; }
        for (var i = 0; i < pending.Length; i++) pending[i].Chunk.SetEmbedding(vectors[i], embeddingKey);
        await db.SaveChangesAsync(ct);
    }
}
