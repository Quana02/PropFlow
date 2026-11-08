using System.Text;

namespace PropFlow.Modules.Communication.Domain.SupportChat;

public sealed class SupportConversation
{
    private SupportConversation() { }
    public SupportConversation(Guid residentUserId, Guid residentId, string name, DateTimeOffset now)
    { Id = Guid.NewGuid(); ResidentUserId = residentUserId; ResidentId = residentId; ResidentName = name; CreatedAt = UpdatedAt = now; }
    public Guid Id { get; private set; }
    public Guid ResidentUserId { get; private set; }
    public Guid ResidentId { get; private set; }
    public string ResidentName { get; private set; } = "";
    public Guid? ManagerUserId { get; private set; }
    public string? ManagerName { get; private set; }
    public string Status { get; private set; } = "WAITING";
    public long LastSequence { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public void Assign(Guid manager, string name, DateTimeOffset now)
    {
        if (Status != "WAITING") throw new InvalidOperationException("Cuộc trò chuyện không còn trong hàng chờ.");
        ManagerUserId = manager; ManagerName = name; Status = "ACTIVE"; UpdatedAt = now;
    }
    public SupportMessage Message(Guid? sender, string name, string kind, Guid clientId, string text, DateTimeOffset now)
    {
        if (Status == "CLOSED") throw new InvalidOperationException("Cuộc trò chuyện đã kết thúc.");
        if (kind == "MANAGER" && (Status != "ACTIVE" || sender != ManagerUserId)) throw new InvalidOperationException("Chưa được phân công hỗ trợ.");
        if (kind == "RESIDENT" && sender != ResidentUserId) throw new InvalidOperationException("Không phải người tham gia.");
        text = ValidateText(text); LastSequence++; UpdatedAt = now;
        return new(Id, LastSequence, sender, name, kind, clientId, text, now);
    }
    public void Close(DateTimeOffset now) { Status = "CLOSED"; ClosedAt = now; UpdatedAt = now; }
    public static string ValidateText(string? text)
    {
        text = (text ?? "").Trim().Normalize(NormalizationForm.FormC);
        if (text.Length is < 1 or > 4000 || text.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            throw new ArgumentException("Nhập tin nhắn từ 1 đến 4.000 ký tự hợp lệ.");
        return text;
    }
}

public sealed class SupportMessage
{
    private SupportMessage() { }
    internal SupportMessage(Guid conversationId, long sequence, Guid? senderUserId, string senderName, string kind,
        Guid clientMessageId, string text, DateTimeOffset now)
    { Id = Guid.NewGuid(); ConversationId = conversationId; Sequence = sequence; SenderUserId = senderUserId; SenderName = senderName; Kind = kind; ClientMessageId = clientMessageId; Text = text; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public long Sequence { get; private set; }
    public Guid? SenderUserId { get; private set; }
    public string SenderName { get; private set; } = "";
    public string Kind { get; private set; } = "";
    public Guid ClientMessageId { get; private set; }
    public string Text { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class SupportManagerPresence
{
    private SupportManagerPresence() { }
    public SupportManagerPresence(Guid userId) => UserId = userId;
    public Guid UserId { get; private set; }
    public bool Available { get; private set; }
    public DateTimeOffset? LastAssignedAt { get; private set; }
    public void SetAvailable(bool value) => Available = value;
    public void Assigned(DateTimeOffset now) => LastAssignedAt = now;
}
public sealed class SupportManagerConnection
{
    private SupportManagerConnection() { }
    public SupportManagerConnection(string id, Guid manager, DateTimeOffset now) { Id = id; ManagerUserId = manager; Renew(now); }
    public string Id { get; private set; } = "";
    public Guid ManagerUserId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public void Renew(DateTimeOffset now) => ExpiresAt = now.AddSeconds(75);
}
