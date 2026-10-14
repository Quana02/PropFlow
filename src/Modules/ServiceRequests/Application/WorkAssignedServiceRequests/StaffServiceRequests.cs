using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;

public enum StaffServiceRequestOutcome { Success, InvalidInput, NotFound, Conflict }

public sealed record StaffServiceRequestResult<T>(
    StaffServiceRequestOutcome Outcome,
    T? Value = default,
    string? ErrorCode = null,
    string? Message = null);

public sealed record StaffServiceRequestCore(
    Guid Id,
    string RequestNumber,
    string Title,
    string Description,
    string? CategoryName,
    Guid? ApartmentUnitId,
    Guid? FacilityId,
    Guid? EquipmentId,
    string Status,
    string? PriorityCode,
    string? ServiceAreaCode,
    DateOnly? PreferredDate,
    string? PreferredTimeCode,
    Guid AssignmentId,
    DateTimeOffset AssignedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt);

public sealed record StaffServiceRequestPage(
    IReadOnlyList<StaffServiceRequestCore> Items,
    int TotalCount,
    StaffServiceRequestSummary Summary);

public sealed record StaffEvidenceMetadata(
    Guid Id,
    Guid ServiceRequestId,
    Guid ActivityId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string StorageKey,
    Guid UploadedBy,
    DateTimeOffset UploadedAt);

public sealed record StaffEvidenceUpload(string FileName, string ContentType, long SizeBytes, Stream Content);
public sealed record StoredEvidence(string StorageKey, long SizeBytes);
public sealed record StaffEvidenceDownload(Stream Content, string ContentType, string FileName);

public interface IStaffServiceRequestStore
{
    Task<StaffServiceRequestPage> ListAsync(Guid staffUserId, StaffServiceRequestListQuery query, CancellationToken ct);
    Task<StaffServiceRequestCore?> GetByNumberAsync(Guid staffUserId, string requestNumber, CancellationToken ct);
    Task<StaffServiceRequestCore?> GetByIdAsync(Guid staffUserId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<StaffServiceRequestActivityItem>> ActivitiesAsync(Guid staffUserId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<StaffEvidenceMetadata>> EvidenceAsync(Guid staffUserId, Guid id, CancellationToken ct);
    Task<Guid?> PendingWorkResultActivityIdAsync(Guid staffUserId, Guid id, CancellationToken ct);
    Task<StaffEvidenceMetadata?> EvidenceFileAsync(Guid staffUserId, Guid requestId, Guid evidenceId, CancellationToken ct);
    Task<StaffServiceRequestOutcome> AcceptAsync(Guid staffUserId, Guid id, DateTimeOffset now, CancellationToken ct);
    Task<StaffServiceRequestOutcome> AddProgressAsync(Guid staffUserId, Guid id, string detail, DateTimeOffset now, CancellationToken ct);
    Task<StaffServiceRequestOutcome> CompleteWorkAsync(Guid staffUserId, Guid id, string workResult, DateTimeOffset now, CancellationToken ct);
    Task<StaffServiceRequestOutcome> AddEvidenceAsync(Guid staffUserId, Guid id, Guid activityId, IReadOnlyList<StaffEvidenceMetadata> evidence, DateTimeOffset now, CancellationToken ct);
}

public interface IServiceRequestEvidenceStorage
{
    Task<StoredEvidence> SaveAsync(Guid serviceRequestId, string fileName, string contentType, long sizeBytes, Stream content, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
}

public sealed class StaffServiceRequestHandler(
    IStaffServiceRequestStore store,
    IServiceRequestEvidenceStorage storage,
    IApartmentResidentRelationshipSource apartments,
    IMaintenanceAssetSource assets,
    TimeProvider clock)
{
    public async Task<StaffServiceRequestResult<StaffServiceRequestListResponse>> ListAsync(
        Guid staffUserId, StaffServiceRequestListQuery query, CancellationToken ct)
    {
        var normalized = Normalize(query);
        if (staffUserId == Guid.Empty || normalized is null)
            return Invalid<StaffServiceRequestListResponse>("invalid_staff_service_request_query", "Bộ lọc hoặc phân trang không hợp lệ.");

        var page = await store.ListAsync(staffUserId, normalized, ct);
        var apartmentMap = (await apartments.GetApartmentsAsync(
            page.Items.Where(x => x.ApartmentUnitId.HasValue).Select(x => x.ApartmentUnitId!.Value).Distinct().ToArray(), ct)).ToDictionary(x => x.Id);
        var assetMap = await AssetMapAsync(page.Items, ct);
        var items = page.Items.Select(x => ToListItem(x, apartmentMap, assetMap)).ToArray();
        return new(StaffServiceRequestOutcome.Success,
            new StaffServiceRequestListResponse(items, page.TotalCount, normalized.Page, normalized.PageSize, page.Summary));
    }

    public async Task<StaffServiceRequestResult<StaffServiceRequestDetailResponse>> GetAsync(
        Guid staffUserId, string requestNumber, CancellationToken ct)
    {
        if (staffUserId == Guid.Empty || string.IsNullOrWhiteSpace(requestNumber)) return NotFound<StaffServiceRequestDetailResponse>();
        var item = await store.GetByNumberAsync(staffUserId, requestNumber.Trim(), ct);
        return item is null ? NotFound<StaffServiceRequestDetailResponse>() : await DetailAsync(staffUserId, item, ct);
    }

    public Task<StaffServiceRequestResult<StaffServiceRequestDetailResponse>> AcceptAsync(Guid staffUserId, Guid id, CancellationToken ct) =>
        MutateAsync(staffUserId, id, () => store.AcceptAsync(staffUserId, id, clock.GetUtcNow(), ct),
            "service_request_not_assigned", "Chỉ yêu cầu đang được phân công cho bạn mới có thể tiếp nhận.", ct);

    public async Task<StaffServiceRequestResult<StaffServiceRequestDetailResponse>> AddProgressAsync(
        Guid staffUserId, Guid id, UpdateStaffServiceRequestProgressRequest request, CancellationToken ct)
    {
        var detail = request.Detail?.Trim();
        if (string.IsNullOrWhiteSpace(detail) || detail.Length is < 3 or > 2000)
            return Invalid<StaffServiceRequestDetailResponse>("invalid_progress_detail", "Nội dung tiến độ phải có từ 3 đến 2000 ký tự.");
        return await MutateAsync(staffUserId, id,
            () => store.AddProgressAsync(staffUserId, id, detail, clock.GetUtcNow(), ct),
            "service_request_not_in_progress", "Chỉ yêu cầu bạn đang thực hiện mới có thể cập nhật tiến độ.", ct);
    }

    public async Task<StaffServiceRequestResult<StaffServiceRequestDetailResponse>> CompleteWorkAsync(
        Guid staffUserId, Guid id, CompleteStaffServiceRequestWorkRequest request, CancellationToken ct)
    {
        var result = request.WorkResult?.Trim();
        if (string.IsNullOrWhiteSpace(result) || result.Length is < 10 or > 4000)
            return Invalid<StaffServiceRequestDetailResponse>("invalid_work_result", "Kết quả xử lý phải có từ 10 đến 4000 ký tự.");
        return await MutateAsync(staffUserId, id,
            () => store.CompleteWorkAsync(staffUserId, id, result, clock.GetUtcNow(), ct),
            "service_request_work_result_conflict", "Yêu cầu chưa được tiếp nhận hoặc đã có kết quả đang chờ tải bằng chứng.", ct);
    }

    public async Task<StaffServiceRequestResult<StaffServiceRequestDetailResponse>> UploadEvidenceAsync(
        Guid staffUserId, Guid id, IReadOnlyList<StaffEvidenceUpload> files, CancellationToken ct)
    {
        if (staffUserId == Guid.Empty || id == Guid.Empty || files.Count is < 1 or > 5)
            return Invalid<StaffServiceRequestDetailResponse>("invalid_evidence_count", "Cần chọn từ 1 đến 5 ảnh bằng chứng.");
        if (files.Any(x => x.SizeBytes is <= 0 or > 10 * 1024 * 1024 || x.FileName.Length > 255))
            return Invalid<StaffServiceRequestDetailResponse>("invalid_evidence_file", "Mỗi ảnh không được vượt quá 10 MB và phải có tên tệp hợp lệ.");

        var activityId = await store.PendingWorkResultActivityIdAsync(staffUserId, id, ct);
        if (!activityId.HasValue)
            return new(StaffServiceRequestOutcome.Conflict, ErrorCode: "work_result_not_available", Message: "Cần xác nhận hoàn thành công việc trước khi tải ảnh bằng chứng.");

        var now = clock.GetUtcNow();
        var saved = new List<(StaffEvidenceMetadata Metadata, string StorageKey)>();
        try
        {
            foreach (var file in files)
            {
                var stored = await storage.SaveAsync(id, file.FileName, file.ContentType, file.SizeBytes, file.Content, ct);
                var metadata = new StaffEvidenceMetadata(Guid.NewGuid(), id, activityId.Value, Path.GetFileName(file.FileName),
                    file.ContentType.ToLowerInvariant(), stored.SizeBytes, stored.StorageKey, staffUserId, now);
                saved.Add((metadata, stored.StorageKey));
            }

            var outcome = await store.AddEvidenceAsync(staffUserId, id, activityId.Value, saved.Select(x => x.Metadata).ToArray(), now, ct);
            if (outcome != StaffServiceRequestOutcome.Success)
            {
                await DeleteSavedAsync(saved, ct);
                return outcome == StaffServiceRequestOutcome.NotFound
                    ? NotFound<StaffServiceRequestDetailResponse>()
                    : new(outcome, ErrorCode: "evidence_upload_conflict", Message: "Kết quả xử lý đã thay đổi nên không thể lưu ảnh bằng chứng.");
            }
        }
        catch (ArgumentException exception)
        {
            await DeleteSavedAsync(saved, ct);
            return Invalid<StaffServiceRequestDetailResponse>("invalid_evidence_file", exception.Message);
        }
        catch
        {
            await DeleteSavedAsync(saved, ct);
            throw;
        }

        var core = await store.GetByIdAsync(staffUserId, id, ct);
        return core is null ? NotFound<StaffServiceRequestDetailResponse>() : await DetailAsync(staffUserId, core, ct);
    }

    public async Task<StaffServiceRequestResult<StaffEvidenceDownload>> DownloadEvidenceAsync(
        Guid staffUserId, Guid requestId, Guid evidenceId, CancellationToken ct)
    {
        var evidence = await store.EvidenceFileAsync(staffUserId, requestId, evidenceId, ct);
        if (evidence is null) return NotFound<StaffEvidenceDownload>();
        var stream = await storage.OpenReadAsync(evidence.StorageKey, ct);
        return stream is null
            ? NotFound<StaffEvidenceDownload>()
            : new(StaffServiceRequestOutcome.Success, new StaffEvidenceDownload(stream, evidence.ContentType, evidence.FileName));
    }

    private async Task<StaffServiceRequestResult<StaffServiceRequestDetailResponse>> MutateAsync(
        Guid staffUserId, Guid id, Func<Task<StaffServiceRequestOutcome>> action, string conflictCode, string conflictMessage, CancellationToken ct)
    {
        if (staffUserId == Guid.Empty || id == Guid.Empty) return NotFound<StaffServiceRequestDetailResponse>();
        var outcome = await action();
        if (outcome == StaffServiceRequestOutcome.NotFound) return NotFound<StaffServiceRequestDetailResponse>();
        if (outcome == StaffServiceRequestOutcome.Conflict)
            return new(outcome, ErrorCode: conflictCode, Message: conflictMessage);
        var item = await store.GetByIdAsync(staffUserId, id, ct);
        return item is null ? NotFound<StaffServiceRequestDetailResponse>() : await DetailAsync(staffUserId, item, ct);
    }

    private async Task<StaffServiceRequestResult<StaffServiceRequestDetailResponse>> DetailAsync(
        Guid staffUserId, StaffServiceRequestCore item, CancellationToken ct)
    {
        var apartmentsById = (await apartments.GetApartmentsAsync(item.ApartmentUnitId.HasValue ? [item.ApartmentUnitId.Value] : [], ct)).ToDictionary(x => x.Id);
        var assetsById = await AssetMapAsync([item], ct);
        var apartment = item.ApartmentUnitId.HasValue ? apartmentsById.GetValueOrDefault(item.ApartmentUnitId.Value) : null;
        var facility = item.FacilityId.HasValue ? assetsById.GetValueOrDefault(item.FacilityId.Value) : null;
        var equipment = item.EquipmentId.HasValue ? assetsById.GetValueOrDefault(item.EquipmentId.Value) : null;
        var activities = await store.ActivitiesAsync(staffUserId, item.Id, ct);
        var evidence = (await store.EvidenceAsync(staffUserId, item.Id, ct)).Select(x => new StaffServiceRequestEvidenceItem(
            x.Id, x.ActivityId, x.FileName, x.ContentType, x.SizeBytes, x.UploadedAt,
            $"/api/v1/staff/service-requests/{item.Id}/evidence/{x.Id}")).ToArray();
        return new(StaffServiceRequestOutcome.Success, new StaffServiceRequestDetailResponse(
            item.Id, item.RequestNumber, item.Title, item.Description, item.CategoryName,
            apartment?.UnitNumber, apartment?.FloorNumber, facility?.Name, equipment?.Name,
            item.Status, item.PriorityCode, item.ServiceAreaCode, item.PreferredDate, item.PreferredTimeCode,
            item.AssignedAt, item.StartedAt, item.UpdatedAt, activities, evidence));
    }

    private async Task<Dictionary<Guid, MaintenanceAssetReference>> AssetMapAsync(IEnumerable<StaffServiceRequestCore> items, CancellationToken ct)
    {
        var result = new Dictionary<Guid, MaintenanceAssetReference>();
        foreach (var id in items.Where(x => x.FacilityId.HasValue).Select(x => x.FacilityId!.Value).Distinct())
            if (await assets.GetFacilityAsync(id, ct) is { } facility) result[id] = facility;
        foreach (var id in items.Where(x => x.EquipmentId.HasValue).Select(x => x.EquipmentId!.Value).Distinct())
            if (await assets.GetEquipmentAsync(id, ct) is { } equipment) result[id] = equipment;
        return result;
    }

    private static StaffServiceRequestListItem ToListItem(StaffServiceRequestCore item,
        IReadOnlyDictionary<Guid, ApartmentReference> apartments,
        IReadOnlyDictionary<Guid, MaintenanceAssetReference> assets)
    {
        var apartment = item.ApartmentUnitId.HasValue ? apartments.GetValueOrDefault(item.ApartmentUnitId.Value) : null;
        var facility = item.FacilityId.HasValue ? assets.GetValueOrDefault(item.FacilityId.Value) : null;
        var equipment = item.EquipmentId.HasValue ? assets.GetValueOrDefault(item.EquipmentId.Value) : null;
        return new(item.Id, item.RequestNumber, item.Title, item.Description, apartment?.UnitNumber, apartment?.FloorNumber,
            facility?.Name, equipment?.Name, item.Status, item.PriorityCode, item.AssignedAt, item.UpdatedAt);
    }

    private async Task DeleteSavedAsync(IEnumerable<(StaffEvidenceMetadata Metadata, string StorageKey)> saved, CancellationToken ct)
    {
        foreach (var file in saved)
            await storage.DeleteAsync(file.StorageKey, ct);
    }

    private static StaffServiceRequestListQuery? Normalize(StaffServiceRequestListQuery query)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || query.Search?.Trim().Length > 30) return null;
        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim().ToUpperInvariant();
        if (status is not null && status is not ("ASSIGNED" or "IN_PROGRESS" or "RESOLVED")) return null;
        return query with { Search = query.Search?.Trim().TrimStart('#'), Status = status };
    }

    private static StaffServiceRequestResult<T> Invalid<T>(string code, string message) =>
        new(StaffServiceRequestOutcome.InvalidInput, ErrorCode: code, Message: message);
    private static StaffServiceRequestResult<T> NotFound<T>() =>
        new(StaffServiceRequestOutcome.NotFound, ErrorCode: "assigned_service_request_not_found", Message: "Không tìm thấy yêu cầu được phân công cho bạn.");
}
