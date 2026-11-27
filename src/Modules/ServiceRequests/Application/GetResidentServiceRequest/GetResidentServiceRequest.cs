using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;
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
    Task<StaffEvidenceMetadata?> EvidenceAsync(Guid residentId, Guid requestId, Guid evidenceId, CancellationToken ct) => Task.FromResult<StaffEvidenceMetadata?>(null);
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
    IServiceRequestBilling? billing = null,
    IServiceRequestEvidenceStorage? evidenceStorage = null,
    IPaymentNoticeSource? paymentNotices = null)
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
            {
                detail = detail with { Settlement = new ResidentServiceRequestSettlement(
                    settlement.InvoiceId, settlement.InvoiceNumber, settlement.TotalAmount,
                    settlement.ConfirmedPaidAmount, settlement.OutstandingAmount, settlement.Status) };
                if (paymentNotices is not null)
                {
                    var history = await billing.ListForResidentAsync(serviceRequestId, residence.ResidentId, ct);
                    var invoiceNumbers = history.ToDictionary(x => x.InvoiceId, x => x.InvoiceNumber);
                    var confirmed = (await paymentNotices.ListAsync(history.Select(x => x.InvoiceId).ToArray(), ct)).Where(x => x.Status == "CONFIRMED");
                    detail = detail with { Activities = detail.Activities.Concat(confirmed.Select(x =>
                        new ResidentServiceRequestActivityItem(x.Id, "PAYMENT_CONFIRMED", null, null, "Đã xác nhận thanh toán",
                            $"Đã ghi nhận {x.Amount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))} đ. Phiếu thu {x.Number} · Hóa đơn {invoiceNumbers[x.InvoiceId]}.",
                            x.ConfirmedAt ?? x.Date))).OrderBy(x => x.CreatedAt).ToArray() };
                }
            }
        }
        return new(GetResidentServiceRequestOutcome.Success, detail);
    }

    public async Task<StaffEvidenceDownload?> DownloadEvidenceAsync(Guid userId, Guid requestId, Guid evidenceId, CancellationToken ct)
    {
        var detail = await HandleAsync(userId, requestId, ct);
        if (detail.Detail?.Evidence?.Any(x => x.Id == evidenceId) != true || evidenceStorage is null) return null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await buildingTimeZone.GetAsync(ct));
        var residence = await residences.FindActiveAsync(userId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime), ct);
        if (residence is null) return null;
        var evidence = await store.EvidenceAsync(residence.ResidentId, requestId, evidenceId, ct);
        if (evidence is null) return null;
        var stream = await evidenceStorage.OpenReadAsync(evidence.StorageKey, ct);
        return stream is null ? null : new(stream, evidence.ContentType, evidence.FileName);
    }

    private static GetResidentServiceRequestResult NotFound() =>
        new(
            GetResidentServiceRequestOutcome.RequestNotFound,
            ErrorCode: "service_request_not_found",
            Message: "Không tìm thấy yêu cầu thuộc tài khoản của bạn.");
}
