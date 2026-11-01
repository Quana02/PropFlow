namespace PropFlow.Modules.Communication.Domain.Notifications;
public sealed class FinanceEmail
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid? UserId { get; set; }
    public string Address { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Html { get; set; } = "";
    public string? AttachmentFileName { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
