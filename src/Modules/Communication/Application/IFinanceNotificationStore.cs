namespace PropFlow.Modules.Communication.Application;
public sealed record FinanceNotification(Guid EventId, Guid? UserId, string? Email, string Title,
    string Message, string Html, string SourceType, Guid SourceId, string ActionPath, string? AttachmentFileName = null,
    Guid? PreviousRecipientEventId = null, Guid? SenderUserId = null);
public interface IFinanceNotificationStore
{
    Task PublishAsync(FinanceNotification notification, CancellationToken ct);
}

public interface IFinanceEmailRecipientDirectory
{
    Task<string?> RecipientEmailAsync(Guid? userId, string? snapshotEmail, CancellationToken ct);
}
