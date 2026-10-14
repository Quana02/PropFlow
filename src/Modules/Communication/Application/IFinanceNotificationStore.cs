namespace PropFlow.Modules.Communication.Application;
public sealed record FinanceNotification(Guid EventId, Guid? UserId, string? Email, string Title,
    string Message, string Html, string SourceType, Guid SourceId, string ActionPath, string? AttachmentFileName = null, Guid? PreviousRecipientEventId = null);
public interface IFinanceNotificationStore
{
    Task PublishAsync(FinanceNotification notification, CancellationToken ct);
}
