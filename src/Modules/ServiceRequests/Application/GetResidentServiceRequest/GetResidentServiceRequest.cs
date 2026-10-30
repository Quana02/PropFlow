using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.Billing.Contracts;

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
    TimeProvider clock,
    IServiceRequestBilling? billing = null)
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
        if (detail is null) return NotFound();
        if (billing is not null)
        {
            var settlement = await billing.FindForResidentAsync(serviceRequestId, residence.ResidentId, ct);
            if (settlement is not null)
                detail = detail with { Settlement = new ResidentServiceRequestSettlement(
                    settlement.InvoiceId, settlement.InvoiceNumber, settlement.TotalAmount,
                    settlement.ConfirmedPaidAmount, settlement.OutstandingAmount, settlement.Status) };
        }
        return new(GetResidentServiceRequestOutcome.Success, detail);
    }

    private static GetResidentServiceRequestResult NotFound() =>
        new(
            GetResidentServiceRequestOutcome.RequestNotFound,
            ErrorCode: "service_request_not_found",
            Message: "Không tìm thấy yêu cầu thuộc tài khoản của bạn.");
}
