using System.Text;

namespace PropFlow.Modules.AiChatbot.Domain;

public sealed class KnowledgeDocument
{
    private KnowledgeDocument() { }
    public Guid Id { get; private set; }
    public int LatestRevision { get; private set; }
    public int? PublishedRevision { get; private set; }
    public Guid Token { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static KnowledgeDocument Create() => new() { Id = Guid.NewGuid() };

    public KnowledgeRevision Save(string title, string content, string audience, Guid actor, DateTimeOffset now)
    {
        title = (title ?? "").Trim().Normalize(NormalizationForm.FormC);
        content = (content ?? "").Trim().Normalize(NormalizationForm.FormC);
        if (title.Length is < 1 or > 200 || content.Length is < 1 or > 60000 ||
            title.Any(char.IsControl) || content.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
            throw new KnowledgeValidationException("Nhập tiêu đề tối đa 200 ký tự và nội dung tối đa 60.000 ký tự hợp lệ.");
        if (audience is not ("RESIDENT" or "INTERNAL"))
            throw new KnowledgeValidationException("Vui lòng chọn phạm vi cư dân hoặc nội bộ.");
        if (actor == Guid.Empty) throw new KnowledgeValidationException("Tài khoản thực hiện không hợp lệ.");
        LatestRevision++;
        // Saving a draft never replaces the published snapshot.
        Touch(now);
        return new KnowledgeRevision(Id, LatestRevision, title, content, audience, actor, now);
    }

    public void Publish(DateTimeOffset now)
    {
        if (LatestRevision == 0) throw new KnowledgeValidationException("Tài liệu chưa có nội dung.");
        PublishedRevision = LatestRevision;
        Touch(now);
    }

    public void Unpublish(DateTimeOffset now)
    {
        PublishedRevision = null;
        Touch(now);
    }

    private void Touch(DateTimeOffset now) { Token = Guid.NewGuid(); UpdatedAt = now; }
}

public sealed class KnowledgeRevision
{
    private KnowledgeRevision() { }
    internal KnowledgeRevision(Guid documentId, int number, string title, string content, string audience,
        Guid createdBy, DateTimeOffset createdAt)
    { DocumentId = documentId; Number = number; Title = title; Content = content; Audience = audience; CreatedBy = createdBy; CreatedAt = createdAt; }
    public Guid DocumentId { get; private set; }
    public int Number { get; private set; }
    public string Title { get; private set; } = "";
    public string Content { get; private set; } = "";
    public string Audience { get; private set; } = "";
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class KnowledgeAudit
{
    private KnowledgeAudit() { }
    public KnowledgeAudit(Guid documentId, int revision, string action, Guid actor, DateTimeOffset now)
    { Id = Guid.NewGuid(); DocumentId = documentId; Revision = revision; Action = action; ActorId = actor; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int Revision { get; private set; }
    public string Action { get; private set; } = "";
    public Guid ActorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class KnowledgeValidationException(string message) : Exception(message);
