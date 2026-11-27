namespace PropFlow.Modules.Communication.Application;

public interface IManagerNotificationRecipients
{
    Task<IReadOnlyList<Guid>> GetUserIdsAsync(CancellationToken ct);
}
