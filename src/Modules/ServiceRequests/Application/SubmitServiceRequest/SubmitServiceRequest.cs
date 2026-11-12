using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Application.SubmitServiceRequest;

public enum SubmitServiceRequestOutcome
{
    Created,
    InvalidInput,
    ResidentResidenceNotFound,
    CategoryUnavailable
}

public sealed record SubmitServiceRequestResult(
    SubmitServiceRequestOutcome Outcome,
    ResidentServiceRequestCreatedResponse? Response = null,
    string? ErrorCode = null,
    string? Message = null);

public interface IServiceRequestSubmissionStore
{
    Task<Guid?> FindActiveCategoryIdAsync(string categoryCode, CancellationToken ct);

    Task AddAsync(
        ServiceRequest request,
        ServiceRequestActivity initialActivity,
        CancellationToken ct);
}

public sealed class SubmitServiceRequestHandler(
    IServiceRequestSubmissionStore store,
    IResidentResidenceSource residences,
    ICurrentBuildingTimeZone buildingTimeZone,
    TimeProvider clock,
    IServiceRequestRealtimeNotifier? realtime = null,
    IMaintenanceAssetSource? assets = null)
{
    private static readonly IReadOnlySet<string> ServiceAreaCodes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MASTER_BATHROOM", "LIVING_ROOM", "MASTER_BEDROOM", "SECONDARY_BEDROOM",
            "BEDROOM", "KITCHEN", "OTHER_PRIVATE"
        };

    private static readonly IReadOnlySet<string> PreferredTimeCodes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MORNING", "AFTERNOON", "EVENING", "ANYTIME"
        };

    public async Task<IReadOnlyList<ResidentServiceRequestFacility>> GetFacilitiesAsync(CancellationToken ct)
    {
        if (assets is null) return [];
        return (await assets.GetAvailableAsync(ct)).Where(x => x.AssetType == "FACILITY" && x.Status == "ACTIVE")
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new ResidentServiceRequestFacility(x.Id, x.Name)).ToArray();
    }

    public async Task<SubmitServiceRequestResult> HandleAsync(
        Guid userId,
        CreateResidentServiceRequestRequest command,
        CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return Invalid("invalid_user", "Phiên đăng nhập không hợp lệ.");

        var categoryCode = NormalizeOptional(command.CategoryCode);
        var areaCode = NormalizeOptional(command.ServiceAreaCode);
        var priorityCode = NormalizeOptional(command.PriorityCode);
        var preferredTimeCode = string.IsNullOrWhiteSpace(command.PreferredTimeCode)
            ? null
            : command.PreferredTimeCode.Trim().ToUpperInvariant();

        if (areaCode is not null && !ServiceAreaCodes.Contains(areaCode) && !ServiceRequestAreaCodes.IsCommonArea(areaCode))
            return Invalid("invalid_service_area", "Khu vực hỗ trợ không hợp lệ.");
        if (command.FacilityId is { } facilityId &&
            (areaCode != ServiceRequestAreaCodes.CommonArea || assets is null ||
             await assets.GetFacilityAsync(facilityId, ct) is not { Status: "ACTIVE" }))
            return Invalid("invalid_facility", "Tiện ích đã chọn không còn khả dụng. Vui lòng chọn lại.");
        if (priorityCode is not (null or "NORMAL" or "URGENT"))
            return Invalid("invalid_priority", "Mức độ ưu tiên không hợp lệ.");
        if (preferredTimeCode is not null && !PreferredTimeCodes.Contains(preferredTimeCode))
            return Invalid("invalid_preferred_time", "Khung giờ mong muốn không hợp lệ.");

        var description = command.Description?.Trim() ?? string.Empty;
        if (description.Length is < 10 or > 3500)
            return Invalid("invalid_description", "Mô tả phải có từ 10 đến 3500 ký tự.");
        var title = string.IsNullOrWhiteSpace(command.Title)
            ? CreateTitle(description)
            : command.Title.Trim();
        if (title.Length > 200)
            return Invalid("invalid_title", "Tiêu đề không được vượt quá 200 ký tự.");

        var now = clock.GetUtcNow();
        var today = await CurrentBuildingDateAsync(now, ct);
        if (command.PreferredDate is { } preferredDate && preferredDate < today)
            return Invalid("preferred_date_in_past", "Ngày hẹn mong muốn không thể nằm trong quá khứ.");

        var residence = await residences.FindActiveAsync(userId, today, ct);
        if (residence is null)
        {
            return new(
                SubmitServiceRequestOutcome.ResidentResidenceNotFound,
                ErrorCode: "active_residence_not_found",
                Message: "Tài khoản chưa được liên kết với căn hộ đang cư trú.");
        }

        var categoryId = categoryCode is null ? null : await store.FindActiveCategoryIdAsync(categoryCode, ct);
        if (categoryCode is not null && !categoryId.HasValue)
        {
            return new(
                SubmitServiceRequestOutcome.CategoryUnavailable,
                ErrorCode: "service_request_category_unavailable",
                Message: "Loại dịch vụ hiện chưa được cấu hình. Vui lòng liên hệ Ban Quản lý.");
        }

        var requestNumber = $"YC-{today:yyyyMMdd}-{Guid.NewGuid():N}"[..30].ToUpperInvariant();
        var request = new ServiceRequest(
            requestNumber,
            residence.ResidentId,
            residence.ResidentApartmentId,
            title,
            description,
            now,
            apartmentUnitId: residence.ApartmentUnitId,
            facilityId: command.FacilityId,
            categoryId: categoryId,
            finalPriorityCode: priorityCode,
            serviceAreaCode: areaCode,
            preferredDate: command.PreferredDate,
            preferredTimeCode: preferredTimeCode,
            submittedByUserId: userId);
        var activity = new ServiceRequestActivity(
            request.Id,
            ServiceActivityType.CREATED,
            now,
            toStatus: ServiceRequestStatus.SUBMITTED,
            title: "Cư dân đã gửi yêu cầu",
            detail: "Yêu cầu đang chờ Ban Quản lý tiếp nhận.",
            performedBy: userId);

        await store.AddAsync(request, activity, ct);
        if (realtime is not null)
            await realtime.NotifyChangedAsync(request.Id, userId, CancellationToken.None);

        return new(
            SubmitServiceRequestOutcome.Created,
            new ResidentServiceRequestCreatedResponse(
                request.Id,
                request.RequestNumber,
                request.Status.ToString(),
                request.SubmittedAt));
    }

    private async Task<DateOnly> CurrentBuildingDateAsync(DateTimeOffset now, CancellationToken ct)
    {
        var timeZoneId = await buildingTimeZone.GetAsync(ct);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
    }

    private static SubmitServiceRequestResult Invalid(string code, string message) =>
        new(SubmitServiceRequestOutcome.InvalidInput, ErrorCode: code, Message: message);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string CreateTitle(string description)
    {
        var summary = string.Join(" ", description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (summary.Length <= 120) return summary;
        var end = summary.LastIndexOf(' ', 119, 120);
        if (end <= 0) end = char.IsHighSurrogate(summary[118]) ? 118 : 119;
        return summary[..end] + "…";
    }
}
