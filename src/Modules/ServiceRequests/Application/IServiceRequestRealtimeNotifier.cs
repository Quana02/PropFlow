namespace PropFlow.Modules.ServiceRequests.Application;

public interface IServiceRequestRealtimeNotifier
{
    Task NotifyChangedAsync(
        Guid serviceRequestId,
        Guid? residentUserId = null,
        CancellationToken ct = default);
}
