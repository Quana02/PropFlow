using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;

public enum ManagerServiceRequestOutcome { Success, InvalidInput, NotFound, Conflict }

public sealed record ManagerServiceRequestResult<T>(
    ManagerServiceRequestOutcome Outcome,
    T? Value = default,
    string? ErrorCode = null,
    string? Message = null);

public sealed record ManagerServiceRequestCore(
    Guid Id, string RequestNumber, string Title, string Description,
    string? CategoryCode, string? CategoryName, Guid ResidentId, Guid ResidentApartmentId,
    Guid? ApartmentUnitId, Guid? FacilityId, Guid? EquipmentId, string Status,
    string? PriorityCode, string? ServiceAreaCode, DateOnly? PreferredDate,
    string? PreferredTimeCode, DateTimeOffset SubmittedAt, DateTimeOffset UpdatedAt,
    Guid? AssignedStaffUserId);

public sealed record ManagerServiceRequestPage(
    IReadOnlyList<ManagerServiceRequestCore> Items, int TotalCount,
    ManagerServiceRequestSummary Summary);

public interface IManagerServiceRequestStore
{
    Task<ManagerServiceRequestPage> ListAsync(ManagerServiceRequestListQuery query, CancellationToken ct);
    Task<ManagerServiceRequestCore?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ManagerServiceRequestActivityItem>> GetActivitiesAsync(Guid id, CancellationToken ct);
    Task<ManagerServiceRequestOutcome> ReviewAsync(Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct);
    Task<ManagerServiceRequestOutcome> ConfirmPropertyContextAsync(
        Guid id, Guid managerUserId, Guid? apartmentUnitId, Guid? facilityId, Guid? equipmentId,
        string? serviceAreaCode, string? note, DateTimeOffset now, CancellationToken ct);
    Task<ManagerServiceRequestOutcome> AssignAsync(
        Guid id, Guid managerUserId, Guid staffUserId, string? note, DateTimeOffset now, CancellationToken ct);
}

public sealed class ManagerServiceRequestReviewHandler(
    IManagerServiceRequestStore store,
    IResidentApartmentReadSource residents,
    IApartmentResidentRelationshipSource apartments,
    IMaintenanceAssetSource assets,
    IMaintenanceStaffDirectory staffDirectory,
    IManagerServiceRequestInsightSource insights,
    TimeProvider clock)
{
    public async Task<ManagerServiceRequestResult<ManagerServiceRequestListResponse>> ListAsync(
        ManagerServiceRequestListQuery query, CancellationToken ct)
    {
        var normalized = Normalize(query);
        if (normalized is null)
            return Invalid<ManagerServiceRequestListResponse>("invalid_manager_service_request_query", "Bộ lọc hoặc phân trang không hợp lệ.");

        var page = await store.ListAsync(normalized, ct);
        var residentMap = await residents.GetResidentsAsync(page.Items.Select(x => x.ResidentId).Distinct().ToArray(), ct);
        var apartmentMap = (await apartments.GetApartmentsAsync(page.Items.Where(x => x.ApartmentUnitId.HasValue).Select(x => x.ApartmentUnitId!.Value).Distinct().ToArray(), ct))
            .ToDictionary(x => x.Id);
        var staffMap = (await staffDirectory.GetHistoricalStaffAsync(page.Items
                .Where(x => x.AssignedStaffUserId.HasValue)
                .Select(x => x.AssignedStaffUserId!.Value)
                .Distinct()
                .ToArray(), ct))
            .ToDictionary(x => x.UserId);
        var items = page.Items.Select(x => ToListItem(x, residentMap, apartmentMap, staffMap)).ToArray();
        return new(ManagerServiceRequestOutcome.Success,
            new(items, page.TotalCount, normalized.Page, normalized.PageSize, page.Summary));
    }

    public async Task<ManagerServiceRequestResult<ManagerServiceRequestDetailResponse>> GetAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) return NotFound<ManagerServiceRequestDetailResponse>();
        var item = await store.GetAsync(id, ct);
        if (item is null) return NotFound<ManagerServiceRequestDetailResponse>();

        var residentMap = await residents.GetResidentsAsync([item.ResidentId], ct);
        var apartmentMap = (await apartments.GetApartmentsAsync(item.ApartmentUnitId is { } apartmentId ? [apartmentId] : [], ct)).ToDictionary(x => x.Id);
        var facility = item.FacilityId is { } facilityId ? await assets.GetFacilityAsync(facilityId, ct) : null;
        var equipment = item.EquipmentId is { } equipmentId ? await assets.GetEquipmentAsync(equipmentId, ct) : null;
        var insight = await insights.GetAsync(id, ct);
        var activities = await store.GetActivitiesAsync(id, ct);
        residentMap.TryGetValue(item.ResidentId, out var resident);
        var apartment = item.ApartmentUnitId is { } unitId && apartmentMap.TryGetValue(unitId, out var value) ? value : null;

        return new(ManagerServiceRequestOutcome.Success, new ManagerServiceRequestDetailResponse(
            item.Id, item.RequestNumber, item.Title, item.Description, item.CategoryCode, item.CategoryName,
            item.ResidentId, resident?.ResidentCode, resident?.FullName, item.ResidentApartmentId,
            item.ApartmentUnitId, apartment?.UnitNumber, apartment?.FloorNumber, item.Status, item.PriorityCode,
            item.ServiceAreaCode, item.PreferredDate, item.PreferredTimeCode, item.SubmittedAt, item.UpdatedAt,
            item.AssignedStaffUserId, ToAsset(facility), ToAsset(equipment), insight, activities));
    }

    public async Task<ManagerServiceRequestResult<ManagerServiceRequestDetailResponse>> ReviewAsync(
        Guid id, Guid managerUserId, ReviewManagerServiceRequestRequest request, CancellationToken ct)
    {
        if (id == Guid.Empty || managerUserId == Guid.Empty)
            return Invalid<ManagerServiceRequestDetailResponse>("invalid_review_request", "Thông tin tiếp nhận yêu cầu không hợp lệ.");
        if (request.Note?.Trim().Length > 2000)
            return Invalid<ManagerServiceRequestDetailResponse>("review_note_too_long", "Ghi chú tiếp nhận không được vượt quá 2000 ký tự.");

        var outcome = await store.ReviewAsync(id, managerUserId, request.Note?.Trim(), clock.GetUtcNow(), ct);
        if (outcome == ManagerServiceRequestOutcome.NotFound) return NotFound<ManagerServiceRequestDetailResponse>();
        if (outcome == ManagerServiceRequestOutcome.Conflict)
            return new(outcome, ErrorCode: "service_request_not_submitted", Message: "Chỉ yêu cầu đang chờ tiếp nhận mới có thể chuyển sang xem xét.");
        return await GetAsync(id, ct);
    }

    public async Task<ManagerServiceRequestResult<ManagerPropertyContextOptionsResponse>> PropertyContextOptionsAsync(
        Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty || await store.GetAsync(id, ct) is null)
            return NotFound<ManagerPropertyContextOptionsResponse>();

        var apartmentItems = await apartments.SearchApartmentsAsync(null, 200, ct);
        var assetItems = await assets.GetAvailableAsync(ct);
        return new(ManagerServiceRequestOutcome.Success, new ManagerPropertyContextOptionsResponse(
            apartmentItems.Select(x => new ManagerPropertyContextOption(
                x.Id, x.UnitNumber, $"Căn hộ {x.UnitNumber}", $"Tầng {x.FloorNumber}", "ACTIVE")).ToArray(),
            assetItems.Where(x => x.AssetType == "FACILITY").Select(ToContextOption).ToArray(),
            assetItems.Where(x => x.AssetType == "EQUIPMENT").Select(ToContextOption).ToArray()));
    }

    public async Task<ManagerServiceRequestResult<ManagerServiceRequestAssignmentOptionsResponse>> AssignmentOptionsAsync(
        Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty || await store.GetAsync(id, ct) is null)
            return NotFound<ManagerServiceRequestAssignmentOptionsResponse>();

        var staff = await staffDirectory.GetActiveStaffAsync(ct);
        return new(ManagerServiceRequestOutcome.Success,
            new ManagerServiceRequestAssignmentOptionsResponse(staff
                .Select(x => new ManagerServiceRequestStaffOption(x.UserId, x.Username, x.DisplayName))
                .ToArray()));
    }

    public async Task<ManagerServiceRequestResult<ManagerServiceRequestDetailResponse>> AssignAsync(
        Guid id, Guid managerUserId, AssignManagerServiceRequestRequest request, CancellationToken ct)
    {
        if (id == Guid.Empty || managerUserId == Guid.Empty || request.StaffUserId == Guid.Empty)
            return Invalid<ManagerServiceRequestDetailResponse>("invalid_assignment", "Thông tin phân công nhân sự không hợp lệ.");
        if (request.Note?.Trim().Length > 2000)
            return Invalid<ManagerServiceRequestDetailResponse>("assignment_note_too_long", "Ghi chú phân công không được vượt quá 2000 ký tự.");

        var staff = await staffDirectory.GetActiveStaffAsync(request.StaffUserId, ct);
        if (staff is null)
            return Invalid<ManagerServiceRequestDetailResponse>("active_staff_not_found", "Nhân sự đã chọn không tồn tại, không hoạt động hoặc không còn vai trò Staff.");

        var outcome = await store.AssignAsync(
            id, managerUserId, staff.UserId, request.Note?.Trim(), clock.GetUtcNow(), ct);
        if (outcome == ManagerServiceRequestOutcome.NotFound)
            return NotFound<ManagerServiceRequestDetailResponse>();
        if (outcome == ManagerServiceRequestOutcome.Conflict)
            return new(outcome, ErrorCode: "service_request_assignment_conflict",
                Message: "Yêu cầu chưa được tiếp nhận, đã hoàn tất hoặc nhân sự này đang được phân công.");
        return await GetAsync(id, ct);
    }

    public async Task<ManagerServiceRequestResult<ManagerServiceRequestDetailResponse>> ConfirmPropertyContextAsync(
        Guid id, Guid managerUserId, ConfirmManagerServiceRequestPropertyContextRequest request, CancellationToken ct)
    {
        if (id == Guid.Empty || managerUserId == Guid.Empty)
            return Invalid<ManagerServiceRequestDetailResponse>("invalid_property_context", "Thông tin xác nhận đơn dịch vụ không hợp lệ.");
        if (request.ApartmentUnitId is null && request.FacilityId is null && request.EquipmentId is null && string.IsNullOrWhiteSpace(request.ServiceAreaCode))
            return Invalid<ManagerServiceRequestDetailResponse>("property_context_required", "Cần chọn ít nhất một căn hộ, khu vực, thiết bị hoặc vị trí dịch vụ.");
        if (request.ServiceAreaCode?.Trim().Length > 50)
            return Invalid<ManagerServiceRequestDetailResponse>("service_area_too_long", "Mã khu vực dịch vụ không được vượt quá 50 ký tự.");
        if (request.Note?.Trim().Length > 2000)
            return Invalid<ManagerServiceRequestDetailResponse>("property_context_note_too_long", "Ghi chú không được vượt quá 2000 ký tự.");

        var current = await store.GetAsync(id, ct);
        if (current is null) return NotFound<ManagerServiceRequestDetailResponse>();
        if (current.Status != "SUBMITTED")
            return new(ManagerServiceRequestOutcome.Conflict, ErrorCode: "service_request_not_submitted", Message: "Chỉ yêu cầu đang chờ tiếp nhận mới có thể xác nhận thành đơn dịch vụ.");

        if (request.ApartmentUnitId is { } apartmentId &&
            (await apartments.GetApartmentsAsync([apartmentId], ct)).Count == 0)
            return Invalid<ManagerServiceRequestDetailResponse>("apartment_not_found", "Căn hộ đã chọn không tồn tại.");

        var equipment = request.EquipmentId is { } equipmentId ? await assets.GetEquipmentAsync(equipmentId, ct) : null;
        if (request.EquipmentId.HasValue && equipment is null)
            return Invalid<ManagerServiceRequestDetailResponse>("equipment_not_found", "Thiết bị đã chọn không tồn tại.");
        var effectiveFacilityId = request.FacilityId ?? equipment?.FacilityId;
        var facility = effectiveFacilityId is { } facilityId ? await assets.GetFacilityAsync(facilityId, ct) : null;
        if (effectiveFacilityId.HasValue && facility is null)
            return Invalid<ManagerServiceRequestDetailResponse>("facility_not_found", "Khu vực tiện ích đã chọn không tồn tại.");
        if (request.FacilityId.HasValue && equipment?.FacilityId is { } parentId && parentId != request.FacilityId)
            return Invalid<ManagerServiceRequestDetailResponse>("equipment_facility_mismatch", "Thiết bị không thuộc khu vực tiện ích đã chọn.");

        var effectiveServiceAreaCode = string.IsNullOrWhiteSpace(request.ServiceAreaCode)
            ? current.ServiceAreaCode
            : request.ServiceAreaCode.Trim();
        var outcome = await store.ConfirmPropertyContextAsync(
            id, managerUserId, request.ApartmentUnitId, effectiveFacilityId, request.EquipmentId,
            effectiveServiceAreaCode, request.Note?.Trim(), clock.GetUtcNow(), ct);
        if (outcome == ManagerServiceRequestOutcome.NotFound) return NotFound<ManagerServiceRequestDetailResponse>();
        if (outcome == ManagerServiceRequestOutcome.Conflict)
            return new(outcome, ErrorCode: "service_request_not_submitted", Message: "Yêu cầu đã được người khác tiếp nhận hoặc trạng thái vừa thay đổi.");
        return await GetAsync(id, ct);
    }

    private static ManagerServiceRequestListQuery? Normalize(ManagerServiceRequestListQuery query)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100) return null;
        if (query.SubmittedWithinDays is <= 0 or > 3650) return null;
        if (query.Search?.Trim().Length > 200) return null;
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToUpperInvariant();
        var priority = string.IsNullOrWhiteSpace(query.Priority) ? null : query.Priority.Trim().ToUpperInvariant();
        string[] statuses = ["SUBMITTED", "UNDER_REVIEW", "ASSIGNED", "IN_PROGRESS", "RESOLVED", "CLOSED", "CANCELLED"];
        if (status is not null && status is not ("ALL" or "ACTIVE") && !statuses.Contains(status)) return null;
        return query with { Search = query.Search?.Trim(), Status = status == "ALL" ? null : status, Priority = priority };
    }

    private static ManagerServiceRequestListItem ToListItem(ManagerServiceRequestCore x,
        IReadOnlyDictionary<Guid, ResidentLookupItem> residents,
        IReadOnlyDictionary<Guid, ApartmentReference> apartments,
        IReadOnlyDictionary<Guid, MaintenanceStaffRecord> staff)
    {
        residents.TryGetValue(x.ResidentId, out var resident);
        var apartment = x.ApartmentUnitId is { } id && apartments.TryGetValue(id, out var value) ? value : null;
        var assignedStaff = x.AssignedStaffUserId is { } staffId && staff.TryGetValue(staffId, out var staffValue) ? staffValue : null;
        return new(x.Id, x.RequestNumber, x.Title, x.CategoryCode, x.CategoryName, x.ResidentId,
            resident?.ResidentCode, resident?.FullName, x.ApartmentUnitId, apartment?.UnitNumber,
            apartment?.FloorNumber, x.Status, x.PriorityCode, x.SubmittedAt, x.UpdatedAt, x.AssignedStaffUserId,
            assignedStaff?.Username, assignedStaff?.DisplayName);
    }

    private static ManagerServiceRequestAsset? ToAsset(MaintenanceAssetReference? x) => x is null ? null :
        new(x.Id, x.Code, x.Name, x.AssetType, x.Status, x.LocationDescription);
    private static ManagerPropertyContextOption ToContextOption(MaintenanceAssetReference x) =>
        new(x.Id, x.Code, x.Name, x.LocationDescription, x.Status, x.FacilityId);
    private static ManagerServiceRequestResult<T> Invalid<T>(string code, string message) => new(ManagerServiceRequestOutcome.InvalidInput, ErrorCode: code, Message: message);
    private static ManagerServiceRequestResult<T> NotFound<T>() => new(ManagerServiceRequestOutcome.NotFound, ErrorCode: "service_request_not_found", Message: "Không tìm thấy yêu cầu dịch vụ.");
}
