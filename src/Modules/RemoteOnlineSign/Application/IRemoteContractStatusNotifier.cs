namespace PropFlow.Modules.RemoteOnlineSign.Application;

public sealed record RemoteContractStatusChanged(
    Guid ContractId,
    string PreviousStatus,
    string NewStatus,
    int Revision,
    DateTimeOffset UpdatedAtUtc);

public interface IRemoteContractStatusNotifier
{
    Task NotifyAsync(RemoteContractStatusChanged change, CancellationToken ct);
}
