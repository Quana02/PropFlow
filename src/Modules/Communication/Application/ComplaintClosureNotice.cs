namespace PropFlow.Modules.Communication.Application;

public interface IComplaintClosureNotice
{
    Task<FinanceNotificationUpdate?> AddAsync(Guid eventId, Guid complaintId, Guid recipientUserId, DateTimeOffset occurredAt, CancellationToken ct);
}
