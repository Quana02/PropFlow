using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Application.ListResidentServiceRequests;

public enum ListResidentServiceRequestsOutcome
{
    Success,
    InvalidInput,
    ResidentResidenceNotFound
}

public sealed record ListResidentServiceRequestsResult(
    ListResidentServiceRequestsOutcome Outcome,
    IReadOnlyList<ResidentServiceRequestListItem> Items,
    string? ErrorCode = null,
    string? Message = null);

public interface IResidentServiceRequestReadStore
{
    Task<IReadOnlyList<ResidentServiceRequestListItem>> ListAsync(
        Guid residentId,
        ResidentServiceRequestFilter filter,
        CancellationToken ct);
}

public sealed record ResidentServiceRequestFilter(
    string? Search,
    ServiceRequestStatus? Status,
    DateTimeOffset? SubmittedFromInclusive,
    DateTimeOffset? SubmittedToExclusive);

public sealed class ListResidentServiceRequestsHandler(
    IResidentServiceRequestReadStore store,
    IResidentResidenceSource residences,
    ICurrentBuildingTimeZone buildingTimeZone,
    TimeProvider clock)
{
    public Task<ListResidentServiceRequestsResult> HandleAsync(Guid userId, CancellationToken ct) =>
        HandleAsync(userId, new ResidentServiceRequestListQuery(), ct);

    public async Task<ListResidentServiceRequestsResult> HandleAsync(
        Guid userId,
        ResidentServiceRequestListQuery query,
        CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return MissingResidence();

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search?.Length > 200)
            return Invalid("invalid_search", "Từ khóa tìm kiếm không được vượt quá 200 ký tự.");
        if (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate > query.ToDate)
            return Invalid("invalid_date_range", "Ngày bắt đầu không được sau ngày kết thúc.");

        ServiceRequestStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<ServiceRequestStatus>(query.Status.Trim(), true, out var parsedStatus) ||
                !Enum.IsDefined(parsedStatus))
                return Invalid("invalid_status", "Trạng thái yêu cầu không hợp lệ.");
            status = parsedStatus;
        }

        var timeZoneId = await buildingTimeZone.GetAsync(ct);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone).DateTime);
        var residence = await residences.FindActiveAsync(userId, today, ct);
        if (residence is null)
            return MissingResidence();

        var filter = new ResidentServiceRequestFilter(
            search,
            status,
            query.FromDate is { } fromDate ? BuildingDateStartUtc(fromDate, timeZone) : null,
            query.ToDate is { } toDate ? BuildingDateStartUtc(toDate.AddDays(1), timeZone) : null);
        var items = await store.ListAsync(residence.ResidentId, filter, ct);
        return new(ListResidentServiceRequestsOutcome.Success, items);
    }

    private static DateTimeOffset BuildingDateStartUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var localStart = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone));
    }

    private static ListResidentServiceRequestsResult Invalid(string code, string message) =>
        new(ListResidentServiceRequestsOutcome.InvalidInput, [], code, message);

    private static ListResidentServiceRequestsResult MissingResidence() =>
        new(
            ListResidentServiceRequestsOutcome.ResidentResidenceNotFound,
            [],
            "active_residence_not_found",
            "Tài khoản chưa được liên kết với căn hộ đang cư trú.");
}
