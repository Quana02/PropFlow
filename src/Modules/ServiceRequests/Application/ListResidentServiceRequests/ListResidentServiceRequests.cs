using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Application.ListResidentServiceRequests;

public enum ListResidentServiceRequestsOutcome
{
    Success,
    ResidentResidenceNotFound
}

public sealed record ListResidentServiceRequestsResult(
    ListResidentServiceRequestsOutcome Outcome,
    IReadOnlyList<ResidentServiceRequestListItem> Items,
    string? ErrorCode = null,
    string? Message = null);

public interface IResidentServiceRequestReadStore
{
    Task<IReadOnlyList<ResidentServiceRequestListItem>> ListAsync(Guid residentId, CancellationToken ct);
}

public sealed class ListResidentServiceRequestsHandler(
    IResidentServiceRequestReadStore store,
    IResidentResidenceSource residences,
    ICurrentBuildingTimeZone buildingTimeZone,
    TimeProvider clock)
{
    public async Task<ListResidentServiceRequestsResult> HandleAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return MissingResidence();

        var timeZoneId = await buildingTimeZone.GetAsync(ct);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone).DateTime);
        var residence = await residences.FindActiveAsync(userId, today, ct);
        if (residence is null)
            return MissingResidence();

        var items = await store.ListAsync(residence.ResidentId, ct);
        return new(ListResidentServiceRequestsOutcome.Success, items);
    }

    private static ListResidentServiceRequestsResult MissingResidence() =>
        new(
            ListResidentServiceRequestsOutcome.ResidentResidenceNotFound,
            [],
            "active_residence_not_found",
            "Tài khoản chưa được liên kết với căn hộ đang cư trú.");
}
