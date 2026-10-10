using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Application.GetResidentServiceRequest;

public enum GetResidentServiceRequestOutcome
{
    Success,
    ResidentResidenceNotFound,
    RequestNotFound
}

public sealed record GetResidentServiceRequestResult(
    GetResidentServiceRequestOutcome Outcome,
    ResidentServiceRequestDetailResponse? Detail = null,
    string? ErrorCode = null,
    string? Message = null);

public interface IResidentServiceRequestDetailStore
{
    Task<ResidentServiceRequestDetailResponse?> GetAsync(
        Guid residentId,
        Guid serviceRequestId,
        CancellationToken ct);
}

public sealed class GetResidentServiceRequestHandler(
    IResidentServiceRequestDetailStore store,
    IResidentResidenceSource residences,
    ICurrentBuildingTimeZone buildingTimeZone,
    TimeProvider clock)
{
    public async Task<GetResidentServiceRequestResult> HandleAsync(
        Guid userId,
        Guid serviceRequestId,
        CancellationToken ct)
    {
        if (userId == Guid.Empty || serviceRequestId == Guid.Empty)
            return NotFound();

        var timeZoneId = await buildingTimeZone.GetAsync(ct);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), timeZone).DateTime);
        var residence = await residences.FindActiveAsync(userId, today, ct);
        if (residence is null)
        {
            return new(
                GetResidentServiceRequestOutcome.ResidentResidenceNotFound,
                ErrorCode: "active_residence_not_found",
                Message: "Tài khoản chưa được liên kết với căn hộ đang cư trú.");
        }

        var detail = await store.GetAsync(residence.ResidentId, serviceRequestId, ct);
        return detail is null
            ? NotFound()
            : new(GetResidentServiceRequestOutcome.Success, detail);
    }

    private static GetResidentServiceRequestResult NotFound() =>
        new(
            GetResidentServiceRequestOutcome.RequestNotFound,
            ErrorCode: "service_request_not_found",
            Message: "Không tìm thấy yêu cầu thuộc tài khoản của bạn.");
}
