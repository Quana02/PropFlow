namespace PropFlow.Modules.Communication.Application;

public interface IComplaintClosureNotice
{
    Task AddAsync(Guid eventId, Guid complaintId, Guid recipientUserId, DateTimeOffset occurredAt, CancellationToken ct);
}
