using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Application.AcceptResidentServiceRequest;

public enum ResidentCompletionOutcome { Success, InvalidInput, NotFound, Conflict, PaymentRequired, ResidenceNotFound }
public interface IResidentCompletionStore
{
    Task<ResidentCompletionOutcome> DecideAsync(Guid residentId, Guid userId, Guid requestId, ResidentCompletionDecision input, DateTimeOffset now, CancellationToken ct);
}
public sealed class ResidentCompletionHandler(IResidentCompletionStore store, IResidentResidenceSource residences,
    ICurrentBuildingTimeZone zone, TimeProvider clock, IServiceRequestRealtimeNotifier? realtime = null)
{
    public async Task<ResidentCompletionOutcome> HandleAsync(Guid userId, Guid id, ResidentCompletionDecision input, CancellationToken ct)
    {
        if (id == Guid.Empty || userId == Guid.Empty || input.ExpectedResolvedAt == default ||
            !input.Accepted && (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length is < 3 or > 1000))
            return ResidentCompletionOutcome.InvalidInput;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(await zone.GetAsync(ct))).DateTime);
        var residence = await residences.FindActiveAsync(userId, today, ct);
        if (residence is null) return ResidentCompletionOutcome.ResidenceNotFound;
        var result = await store.DecideAsync(residence.ResidentId, userId, id, input, now, ct);
        if (result == ResidentCompletionOutcome.Success && realtime is not null) await realtime.NotifyChangedAsync(id, userId, ct);
        return result;
    }
}
