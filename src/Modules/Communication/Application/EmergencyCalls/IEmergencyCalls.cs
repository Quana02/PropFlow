using PropFlow.Modules.Communication.Contracts;

namespace PropFlow.Modules.Communication.Application.EmergencyCalls;

public interface IEmergencyCallRealtime
{
    Task ChangedAsync(IReadOnlyCollection<Guid> users, CancellationToken ct);
    Task SignalAsync(string connection, EmergencySignal signal, CancellationToken ct);
}
public interface IEmergencyCalls
{
    Task ConnectedAsync(Guid actor, string connection, CancellationToken ct);
    Task HeartbeatAsync(Guid actor, string connection, CancellationToken ct);
    Task DisconnectedAsync(string connection, CancellationToken ct);
    Task<EmergencyCallView> StartAsync(Guid actor, StartEmergencyCall request, CancellationToken ct);
    Task<EmergencyCallView?> ActiveAsync(Guid actor, string connection, CancellationToken ct);
    Task<EmergencyCallView> EndAsync(Guid actor, Guid id, CancellationToken ct);
    Task<EmergencyCallView> RetryAsync(Guid actor, Guid id, CancellationToken ct);
    Task MediaConnectedAsync(Guid actor, string connection, Guid id, int revision, CancellationToken ct);
    Task MediaDisconnectedAsync(Guid actor, string connection, Guid id, int revision, CancellationToken ct);
    Task SignalAsync(Guid actor, string connection, EmergencySignal signal, CancellationToken ct);
    Task DispatchAsync(CancellationToken ct);
}
public interface IEmergencyRtcConfiguration
{
    Task<EmergencyRtcConfiguration> GetAsync(Guid actor, CancellationToken ct);
}
