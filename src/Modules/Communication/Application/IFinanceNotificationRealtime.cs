namespace PropFlow.Modules.Communication.Application;

public sealed record FinanceNotificationUpdate(Guid Id, string Title, string Message, string? ActionPath,
    bool IsRead, DateTimeOffset CreatedAt);

public interface IFinanceNotificationRealtime
{
    Task CreatedAsync(Guid recipientUserId, FinanceNotificationUpdate notice, CancellationToken ct);
}
