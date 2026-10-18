using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Shared;

namespace PropFlow.Modules.Maintenance.Application;

public sealed class MaintenanceService(
    MaintenanceDbContext db,
    IMaintenanceAssetSource assets,
    IMaintenanceStaffDirectory staffDirectory,
    IMaintenanceNotifier notifier,
    TimeProvider? clock = null,
    IServiceRequestMaintenanceSource? serviceRequestSource = null,
    ICurrentBuildingTimeZone? buildingTimeZone = null,
    IMaintenanceEvidenceStorage? evidenceStorage = null,
    IMaintenanceAssetTransactionCoordinator? assetTransaction = null)
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    // Keeps existing application tests and non-web composition roots compatible.
    // The API composition supplies the SignalR notifier through the primary constructor.
    public MaintenanceService(
        MaintenanceDbContext db,
        IMaintenanceAssetSource assets,
        IMaintenanceStaffDirectory staffDirectory,
        TimeProvider? clock = null)
        : this(db, assets, staffDirectory, NoopMaintenanceNotifier.Instance, clock)
    {
    }

    public MaintenanceService(
        MaintenanceDbContext db,
        IMaintenanceAssetSource assets,
        IMaintenanceStaffDirectory staffDirectory,
        IServiceRequestMaintenanceSource serviceRequestSource,
        TimeProvider? clock = null)
        : this(db, assets, staffDirectory, NoopMaintenanceNotifier.Instance, clock, serviceRequestSource)
    {
    }

    public async Task<MaintenanceTaskDto> CreateTaskAsync(CreateMaintenanceTaskCommand command, Guid actor, CancellationToken ct)
    {
        if (!command.IsEmergency)
            return await CreateTaskCoreAsync(command, actor, markAssetUnderMaintenance: false, ct);

        if (command.ScheduleId is not null)
            throw new ArgumentException("Công việc khẩn cấp không được tạo từ lịch bảo trì.", nameof(command));
        if (assetTransaction is null)
            throw new InvalidOperationException("Chưa cấu hình giao dịch đồng bộ công việc bảo trì và trạng thái tài sản.");

        return await assetTransaction.ExecuteAsync(
            token => CreateTaskCoreAsync(command, actor, markAssetUnderMaintenance: true, token), ct);
    }

    private async Task<MaintenanceTaskDto> CreateTaskCoreAsync(CreateMaintenanceTaskCommand command, Guid actor, bool markAssetUnderMaintenance, CancellationToken ct)
    {
        MaintenanceSchedule? schedule = null;
        if (command.ScheduleId.HasValue)
        {
            schedule = await db.MaintenanceSchedules.FindAsync([command.ScheduleId.Value], ct)
                ?? throw new KeyNotFoundException("Không tìm thấy lịch bảo trì nguồn.");
            await EnsureAssetsAsync(schedule.FacilityId, schedule.EquipmentId, ct, requireAnyAsset: false);
        }
        var facilityId = schedule?.FacilityId ?? command.FacilityId;
        var equipmentId = schedule?.EquipmentId ?? command.EquipmentId;
        if (schedule is null) await EnsureAssetsAsync(facilityId, equipmentId, ct, requireAnyAsset: false);
        if (markAssetUnderMaintenance)
            await EnsureEmergencyAssetsCanEnterMaintenanceAsync(facilityId, equipmentId, ct);

        var description = schedule?.Description ?? command.Description;
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Mô tả là bắt buộc.");
        var relatedRequest = command.ServiceRequestId is { } requestId
            ? await GetLinkableServiceRequestAsync(requestId, ct)
            : null;
        var codeObject = await AssetCodeObjectAsync(facilityId, equipmentId, allowCommon: true, ct);
        var codeDate = await BuildingDateAsync(timeProvider.GetUtcNow(), ct);
        var prefix = $"CVBT-{codeObject}-{codeDate:ddMMyy}-";
        EnsureGeneratedCodeLength(prefix + "001", "Mã công việc bảo trì");
        MaintenanceTask? task = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            MaintenanceTask? candidate = null;
            try
            {
                task = await WithAdvisoryLockAsync($"task:{prefix}", async token =>
                {
                    var taskNumber = $"{prefix}{NextSequence(await db.MaintenanceTasks.AsNoTracking().Where(item => item.TaskNumber.StartsWith(prefix)).Select(item => item.TaskNumber).ToArrayAsync(token), prefix):000}";
                    var created = new MaintenanceTask(taskNumber, command.Title, actor, timeProvider.GetUtcNow(), command.ScheduleId,
                        sourceServiceRequestId: relatedRequest?.Id, facilityId: facilityId, equipmentId: equipmentId,
                        description: description, priorityCode: command.PriorityCode,
                        plannedStartAt: schedule?.PlannedStartAt ?? Utc(command.PlannedStartAt), dueAt: schedule?.PlannedEndAt ?? Utc(command.DueAt));
                    candidate = created;
                    db.MaintenanceTasks.Add(created);
                    if (relatedRequest is not null)
                        db.MaintenanceTaskActivities.Add(ServiceRequestLinkedActivity(created.Id, relatedRequest, actor, timeProvider.GetUtcNow()));
                    await db.SaveChangesAsync(token);
                    return created;
                }, ct);
                break;
            }
            catch (DbUpdateException exception) when (candidate is not null && attempt < 2 && IsUniqueViolation(exception))
            {
                db.Entry(candidate).State = EntityState.Detached;
                foreach (var entry in db.ChangeTracker.Entries<MaintenanceTaskActivity>().Where(entry => entry.State == EntityState.Added).ToArray())
                    entry.State = EntityState.Detached;
            }
        }
        if (task is null)
            throw new InvalidOperationException("Không thể cấp mã công việc duy nhất. Vui lòng thử lại.");
        if (markAssetUnderMaintenance)
            await assets.MarkUnderMaintenanceAsync(facilityId, equipmentId, actor, ct);
        return await ToTaskDtoAsync(task, schedule, await MonitorAsync(task, ct), null, ct);
    }

    public async Task<Page<MaintenanceTaskDto>> TasksAsync(TaskQuery query, CancellationToken ct)
    {
        EnsureValidRange(query.From, query.To);
        var source = db.MaintenanceTasks.AsNoTracking().Include(x => x.Schedule).Include(x => x.Assignments).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var key = query.SearchKeyword.Trim().ToLower();
            source = source.Where(x => x.TaskNumber.ToLower().Contains(key) || x.Title.ToLower().Contains(key));
        }
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        if (query.FacilityId.HasValue) source = source.Where(x => x.FacilityId == query.FacilityId);
        if (query.EquipmentId.HasValue) source = source.Where(x => x.EquipmentId == query.EquipmentId);
        if (query.AssignedStaffUserId.HasValue)
            source = source.Where(task => task.Assignments.Any(assignment =>
                assignment.StaffUserId == query.AssignedStaffUserId &&
                (assignment.Status == AssignmentStatus.ASSIGNED || assignment.Status == AssignmentStatus.IN_PROGRESS)));
        // Tasks are included when their known planning interval overlaps the selected range.
        // Tasks without a planned time are excluded only when a time filter is supplied.
        if (query.From.HasValue)
            source = source.Where(task => (task.DueAt ?? task.PlannedStartAt) != null && (task.DueAt ?? task.PlannedStartAt) >= query.From);
        if (query.To.HasValue)
            source = source.Where(task => (task.PlannedStartAt ?? task.DueAt) != null && (task.PlannedStartAt ?? task.DueAt) <= query.To);
        var total = await source.CountAsync(ct);
        var page = Math.Max(1, query.PageIndex);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var items = await source.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var staffById = (await staffDirectory.GetActiveStaffAsync(ct)).ToDictionary(staff => staff.UserId);
        var results = new List<MaintenanceTaskDto>(items.Count);
        foreach (var task in items)
        {
            results.Add(ToDto(task, task.Schedule, CurrentAssignees(task, staffById)));
        }
        return new(results, total, page, size);
    }

    public async Task<MaintenanceTaskDto> TaskAsync(Guid id, CancellationToken ct)
    {
        var task = await db.MaintenanceTasks.AsNoTracking().Include(x => x.Schedule).Include(x => x.Assignments).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy công việc bảo trì.");
        return await ToTaskDtoAsync(task, task.Schedule, await MonitorAsync(task, ct), await LatestResultAsync(task.Id, ct), ct);
    }

    public async Task<MaintenanceTaskDto> LinkTaskServiceRequestAsync(Guid taskId, LinkMaintenanceTaskServiceRequestCommand command, Guid actor, CancellationToken ct)
    {
        if (command.ServiceRequestId == Guid.Empty)
            throw new ArgumentException("Yêu cầu dịch vụ liên quan không hợp lệ.", nameof(command.ServiceRequestId));

        var task = await db.MaintenanceTasks.Include(item => item.Schedule).Include(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == taskId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy công việc bảo trì.");
        var request = await GetLinkableServiceRequestAsync(command.ServiceRequestId, ct);
        task.LinkServiceRequest(request.Id, timeProvider.GetUtcNow());
        db.MaintenanceTaskActivities.Add(ServiceRequestLinkedActivity(task.Id, request, actor, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        await NotifyVisibleAssigneesAsync(task, ct);
        return await ToTaskDtoAsync(task, task.Schedule, await MonitorAsync(task, ct), await LatestResultAsync(task.Id, ct), ct);
    }

    public async Task<Page<MaintenanceTaskDto>> MyTasksAsync(MyMaintenanceTaskQuery query, Guid staffUserId, CancellationToken ct)
    {
        var source = db.MaintenanceTasks.AsNoTracking().Include(task => task.Schedule).Include(task => task.Assignments).AsQueryable();
        if (query.CompletedOnly)
        {
            source = source.Where(task => task.Assignments.Any(assignment => assignment.StaffUserId == staffUserId) &&
                (task.Status == MaintenanceTaskStatus.COMPLETED || task.Status == MaintenanceTaskStatus.CLOSED));
        }
        else
        {
            source = source.Where(task => task.Assignments.Any(assignment => assignment.StaffUserId == staffUserId &&
                (assignment.Status == AssignmentStatus.ASSIGNED || assignment.Status == AssignmentStatus.IN_PROGRESS)));
        }
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var key = query.SearchKeyword.Trim().ToLower();
            source = source.Where(task => task.TaskNumber.ToLower().Contains(key) || task.Title.ToLower().Contains(key));
        }
        if (query.Status.HasValue) source = source.Where(task => task.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.PriorityCode))
        {
            var priority = query.PriorityCode.Trim().ToUpperInvariant();
            source = source.Where(task => task.PriorityCode == priority);
        }

        var total = await source.CountAsync(ct);
        var page = Math.Max(1, query.PageIndex);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var tasks = await source.OrderBy(task => task.DueAt == null).ThenBy(task => task.DueAt).ThenByDescending(task => task.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var staff = await staffDirectory.GetActiveStaffAsync(staffUserId, ct);
        var items = new List<MaintenanceTaskDto>(tasks.Count);
        foreach (var task in tasks)
        {
            items.Add(await ToTaskDtoAsync(task, task.Schedule, await MonitorAsync(task, ct), null, ct));
        }
        return new(items, total, page, size);
    }

    public async Task<MaintenanceTaskDto> MyTaskAsync(Guid id, Guid staffUserId, CancellationToken ct)
    {
        var task = await db.MaintenanceTasks.AsNoTracking().Include(task => task.Schedule).Include(task => task.Assignments)
            .SingleOrDefaultAsync(task => task.Id == id && task.Assignments.Any(assignment => assignment.StaffUserId == staffUserId &&
                (assignment.Status == AssignmentStatus.ASSIGNED || assignment.Status == AssignmentStatus.IN_PROGRESS ||
                 ((task.Status == MaintenanceTaskStatus.COMPLETED || task.Status == MaintenanceTaskStatus.CLOSED) && assignment.Status == AssignmentStatus.COMPLETED))), ct)
            ?? throw new KeyNotFoundException("Không tìm thấy công việc được phân công cho bạn.");
        return await ToTaskDtoAsync(task, task.Schedule, await MonitorAsync(task, ct), await LatestResultAsync(task.Id, ct), ct);
    }

    public async Task<MaintenanceTaskDto> StartMyTaskAsync(Guid taskId, Guid staffUserId, CancellationToken ct)
    {
        var (task, assignment) = await CurrentAssignedTaskAsync(taskId, staffUserId, ct);
        if (task.Status != MaintenanceTaskStatus.ASSIGNED) throw new InvalidOperationException("Không thể bắt đầu công việc ở trạng thái hiện tại.");
        var now = timeProvider.GetUtcNow(); task.Start(now); assignment.Start(now);
        db.MaintenanceTaskActivities.Add(new(task.Id, MaintenanceActivityType.STATUS_CHANGED, now, assignment.Id, MaintenanceTaskStatus.ASSIGNED, MaintenanceTaskStatus.IN_PROGRESS, "Bắt đầu công việc", staffUserId));
        await db.SaveChangesAsync(ct);
        await NotifyVisibleAssigneesAsync(task, ct);
        return await MyTaskAsync(taskId, staffUserId, ct);
    }
    public async Task<MaintenanceTaskDto> UpdateMyTaskProgressAsync(Guid taskId, UpdateMaintenanceProgressCommand command, Guid staffUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Content)) throw new ArgumentException("Vui lòng nhập nội dung cập nhật tiến độ.");
        var (task, assignment) = await CurrentAssignedTaskAsync(taskId, staffUserId, ct);
        if (task.Status != MaintenanceTaskStatus.IN_PROGRESS) throw new InvalidOperationException("Chỉ có thể cập nhật tiến độ khi công việc đang thực hiện.");
        db.MaintenanceTaskActivities.Add(new(task.Id, MaintenanceActivityType.PROGRESS_UPDATED, timeProvider.GetUtcNow(), assignment.Id, detail: command.Content, performedBy: staffUserId));
        await db.SaveChangesAsync(ct);
        await NotifyVisibleAssigneesAsync(task, ct);
        return await MyTaskAsync(taskId, staffUserId, ct);
    }
    public async Task<MaintenanceTaskDto> SubmitMyTaskResultAsync(Guid taskId, SubmitMaintenanceResultCommand command, Guid staffUserId, CancellationToken ct)
        => await SubmitMyTaskResultCoreAsync(taskId, command, staffUserId, [], requireEvidence: false, ct);

    public async Task<MaintenanceTaskDto> SubmitMyTaskResultWithEvidenceAsync(Guid taskId, SubmitMaintenanceResultCommand command, Guid staffUserId, IReadOnlyList<MaintenanceEvidenceUpload> evidenceImages, CancellationToken ct)
        => await SubmitMyTaskResultCoreAsync(taskId, command, staffUserId, evidenceImages, requireEvidence: true, ct);

    private async Task<MaintenanceTaskDto> SubmitMyTaskResultCoreAsync(Guid taskId, SubmitMaintenanceResultCommand command, Guid staffUserId, IReadOnlyList<MaintenanceEvidenceUpload> evidenceImages, bool requireEvidence, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Summary))
            throw new ArgumentException("Vui lòng nhập kết quả thực hiện.");
        if (requireEvidence && (evidenceImages.Count is < 1 or > 5))
            throw new ArgumentException("Vui lòng tải từ 1 đến 5 ảnh minh chứng.");
        if (requireEvidence && evidenceStorage is null)
            throw new InvalidOperationException("Hạ tầng lưu ảnh minh chứng chưa sẵn sàng.");

        var (task, assignment) = await CurrentAssignedTaskAsync(taskId, staffUserId, ct);
        if (task.Status != MaintenanceTaskStatus.IN_PROGRESS)
            throw new InvalidOperationException("Công việc không còn ở trạng thái có thể hoàn thành.");

        var now = timeProvider.GetUtcNow();
        var attemptNo = (await db.MaintenanceResults
            .Where(result => result.MaintenanceTaskId == taskId)
            .Select(result => (int?)result.AttemptNo)
            .MaxAsync(ct) ?? 0) + 1;
        var result = new MaintenanceResult(task.Id, attemptNo, staffUserId, command.Summary, now,
            command.WorkPerformed, command.IssueFound, command.PartsOrResourcesUsed, command.Recommendation);

        db.MaintenanceResults.Add(result);
        var stored = new List<StoredMaintenanceEvidence>();
        await using var transaction = requireEvidence ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            if (requireEvidence)
            {
                foreach (var upload in evidenceImages)
                {
                    var saved = await evidenceStorage!.StoreAsync(result.Id, upload, ct);
                    stored.Add(saved);
                    db.MaintenanceResultAttachments.Add(new MaintenanceResultAttachment(result.Id, saved.StorageKey, saved.OriginalFileName, saved.ContentType, saved.SizeBytes, staffUserId, now));
                }
            }
            task.Complete(now);
            foreach (var activeAssignment in task.Assignments.Where(item => item.IsActive))
                activeAssignment.Complete(now);
        db.MaintenanceTaskActivities.Add(new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.RESULT_SUBMITTED, now,
            assignment.Id, MaintenanceTaskStatus.IN_PROGRESS, MaintenanceTaskStatus.COMPLETED,
            "Đã gửi kết quả để Manager nghiệm thu.", staffUserId));
        await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            if (evidenceStorage is not null)
                foreach (var saved in stored)
                    try { await evidenceStorage.DeleteAsync(saved.StorageKey, CancellationToken.None); }
                    catch { /* Preserve the original failure; orphan cleanup is retriable operational work. */ }
            throw;
        }
        await NotifyVisibleAssigneesAsync(task, ct);
        return await MyTaskAsync(taskId, staffUserId, ct);
    }
    public async Task<IReadOnlyList<MaintenanceTaskActivityDto>> MyTaskActivitiesAsync(Guid taskId, Guid staffUserId, CancellationToken ct)
    {
        await CurrentAssignedTaskAsync(taskId, staffUserId, ct);
        return await db.MaintenanceTaskActivities.AsNoTracking().Where(x => x.MaintenanceTaskId == taskId).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new MaintenanceTaskActivityDto(x.Id, x.ActivityType, x.Detail, x.CreatedAt, x.PerformedBy)).ToListAsync(ct);
    }

    public Task<MaintenanceEvidenceDownload> ResultAttachmentAsync(Guid taskId, Guid resultId, Guid attachmentId, CancellationToken ct) =>
        ResultAttachmentCoreAsync(taskId, resultId, attachmentId, null, ct);

    public Task<MaintenanceEvidenceDownload> MyResultAttachmentAsync(Guid taskId, Guid resultId, Guid attachmentId, Guid staffUserId, CancellationToken ct) =>
        ResultAttachmentCoreAsync(taskId, resultId, attachmentId, staffUserId, ct);

    private async Task<MaintenanceEvidenceDownload> ResultAttachmentCoreAsync(Guid taskId, Guid resultId, Guid attachmentId, Guid? staffUserId, CancellationToken ct)
    {
        if (evidenceStorage is null) throw new InvalidOperationException("Hạ tầng lưu ảnh minh chứng chưa sẵn sàng.");
        if (staffUserId.HasValue && !await db.MaintenanceAssignments.AsNoTracking().AnyAsync(item => item.MaintenanceTaskId == taskId && item.StaffUserId == staffUserId.Value, ct))
            throw new UnauthorizedAccessException("Bạn không có quyền xem ảnh minh chứng của công việc này.");
        var attachment = await db.MaintenanceResultAttachments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == attachmentId && item.MaintenanceResultId == resultId &&
                db.MaintenanceResults.Any(result => result.Id == resultId && result.MaintenanceTaskId == taskId), ct)
            ?? throw new KeyNotFoundException("Không tìm thấy ảnh minh chứng.");
        return new(await evidenceStorage.OpenReadAsync(attachment.StorageKey, ct), attachment.ContentType, attachment.OriginalFileName);
    }
    private async Task<(MaintenanceTask Task, MaintenanceAssignment Assignment)> CurrentAssignedTaskAsync(Guid taskId, Guid staffUserId, CancellationToken ct)
    {
        var task = await db.MaintenanceTasks.Include(x => x.Schedule).Include(x => x.Assignments).SingleOrDefaultAsync(x => x.Id == taskId, ct) ?? throw new KeyNotFoundException("Không tìm thấy công việc bảo trì.");
        var assignment = task.Assignments.SingleOrDefault(x => x.StaffUserId == staffUserId && x.IsActive) ?? throw new UnauthorizedAccessException("Bạn không còn được phân công công việc này.");
        return (task, assignment);
    }

    public async Task<MaintenanceHistoryListDto> HistoryAsync(MaintenanceHistoryQuery query, CancellationToken ct)
    {
        EnsureValidRange(query.From, query.To);
        var tasks = db.MaintenanceTasks.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword))
        {
            var key = query.SearchKeyword.Trim().ToLower();
            tasks = tasks.Where(task => task.TaskNumber.ToLower().Contains(key) || task.Title.ToLower().Contains(key));
        }
        if (query.Status.HasValue) tasks = tasks.Where(task => task.Status == query.Status);
        if (query.FacilityId.HasValue) tasks = tasks.Where(task => task.FacilityId == query.FacilityId);
        if (query.EquipmentId.HasValue) tasks = tasks.Where(task => task.EquipmentId == query.EquipmentId);
        if (query.StaffUserId.HasValue)
            tasks = tasks.Where(task => db.MaintenanceAssignments.Any(assignment => assignment.MaintenanceTaskId == task.Id && assignment.StaffUserId == query.StaffUserId));

        var records = tasks.Select(task => new
        {
            task.Id,
            task.Status,
            HistoryAt = db.MaintenanceTaskActivities.Where(activity => activity.MaintenanceTaskId == task.Id)
                .OrderByDescending(activity => activity.CreatedAt)
                .Select(activity => (DateTimeOffset?)activity.CreatedAt)
                .FirstOrDefault() ?? task.ClosedAt ?? task.CompletedAt ?? task.UpdatedAt
        });
        if (query.From.HasValue) records = records.Where(record => record.HistoryAt >= query.From);
        if (query.To.HasValue) records = records.Where(record => record.HistoryAt <= query.To);

        var counts = await records.GroupBy(record => record.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() }).ToListAsync(ct);
        var summary = new MaintenanceHistorySummaryDto(
            counts.Sum(item => item.Count),
            CountStatus(MaintenanceTaskStatus.OPEN), CountStatus(MaintenanceTaskStatus.ASSIGNED), CountStatus(MaintenanceTaskStatus.IN_PROGRESS),
            CountStatus(MaintenanceTaskStatus.COMPLETED), CountStatus(MaintenanceTaskStatus.CLOSED), CountStatus(MaintenanceTaskStatus.CANCELLED));
        int CountStatus(MaintenanceTaskStatus status) => counts.SingleOrDefault(item => item.Status == status)?.Count ?? 0;

        var total = summary.Total;
        var page = Math.Max(1, query.PageIndex);
        var size = Math.Clamp(query.PageSize, 1, 100);
        records = query.Sort == MaintenanceHistorySort.OLDEST
            ? records.OrderBy(record => record.HistoryAt).ThenBy(record => record.Id)
            : records.OrderByDescending(record => record.HistoryAt).ThenByDescending(record => record.Id);
        var selected = await records.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        if (selected.Count == 0) return new(new([], total, page, size), summary);

        var ids = selected.Select(record => record.Id).ToArray();
        var historyAtByTask = selected.ToDictionary(record => record.Id, record => record.HistoryAt);
        var pageTasks = await db.MaintenanceTasks.AsNoTracking().Include(task => task.Schedule).Include(task => task.Assignments)
            .Where(task => ids.Contains(task.Id)).ToListAsync(ct);
        var latestResults = await db.MaintenanceResults.AsNoTracking().Where(result => ids.Contains(result.MaintenanceTaskId))
            .OrderByDescending(result => result.AttemptNo).ToListAsync(ct);
        var staffById = await HistoricalStaffByIdAsync(pageTasks.SelectMany(task => task.Assignments).Select(assignment => assignment.StaffUserId), ct);
        var byTask = pageTasks.ToDictionary(task => task.Id);
        var resultByTask = latestResults.GroupBy(result => result.MaintenanceTaskId).ToDictionary(group => group.Key, group => group.First());
        var items = selected.Select(record => ToHistoryItem(byTask[record.Id], historyAtByTask[record.Id], resultByTask.GetValueOrDefault(record.Id), staffById)).ToArray();
        return new(new(items, total, page, size), summary);
    }

    public Task<MaintenanceHistoryListDto> MyHistoryAsync(MaintenanceHistoryQuery query, Guid staffUserId, CancellationToken ct) =>
        HistoryAsync(query with { StaffUserId = staffUserId }, ct);

    public async Task<MaintenanceHistoryDetailDto> HistoryDetailAsync(Guid taskId, CancellationToken ct)
    {
        var task = await db.MaintenanceTasks.AsNoTracking().Include(item => item.Schedule).Include(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == taskId, ct) ?? throw new KeyNotFoundException("Không tìm thấy công việc bảo trì.");
        var activities = await db.MaintenanceTaskActivities.AsNoTracking().Where(activity => activity.MaintenanceTaskId == taskId)
            .OrderBy(activity => activity.CreatedAt).ThenBy(activity => activity.Id).ToListAsync(ct);
        var results = await db.MaintenanceResults.AsNoTracking().Where(result => result.MaintenanceTaskId == taskId)
            .OrderBy(result => result.AttemptNo).ToListAsync(ct);
        var staffById = await HistoricalStaffByIdAsync(task.Assignments.Select(assignment => assignment.StaffUserId), ct);
        var historyAt = activities.LastOrDefault()?.CreatedAt ?? task.ClosedAt ?? task.CompletedAt ?? task.UpdatedAt;
        var latest = results.LastOrDefault();
        var item = ToHistoryItem(task, historyAt, latest, staffById);
        var assignments = task.Assignments.OrderBy(assignment => assignment.AssignedAt).ThenBy(assignment => assignment.Id)
            .Select(assignment => ToHistoryAssignment(assignment, staffById.GetValueOrDefault(assignment.StaffUserId))).ToArray();
        var timeline = BuildTimeline(task, activities, results);
        var resultDtos = new List<MaintenanceResultDto>(results.Count);
        foreach (var itemResult in results) resultDtos.Add(await ToDtoAsync(itemResult, ct));
        return new(item, assignments, resultDtos, timeline);
    }

    public async Task<MaintenanceHistoryDetailDto> MyHistoryDetailAsync(Guid taskId, Guid staffUserId, CancellationToken ct)
    {
        var participated = await db.MaintenanceAssignments.AsNoTracking()
            .AnyAsync(assignment => assignment.MaintenanceTaskId == taskId && assignment.StaffUserId == staffUserId, ct);
        if (!participated) throw new KeyNotFoundException("Không tìm thấy lịch sử bảo trì của bạn.");
        return await HistoryDetailAsync(taskId, ct);
    }

    public async Task<MaintenanceTaskDto> ReviewResultAsync(Guid taskId, Guid resultId, ReviewMaintenanceResultCommand command, Guid actor, CancellationToken ct)
    {
        var task = await db.MaintenanceTasks.Include(task => task.Schedule).Include(task => task.Assignments)
            .SingleOrDefaultAsync(task => task.Id == taskId, ct) ?? throw new KeyNotFoundException("Không tìm thấy công việc bảo trì.");
        if (task.Status != MaintenanceTaskStatus.COMPLETED)
            throw new InvalidOperationException("Chỉ có thể review kết quả của công việc đã hoàn thành.");
        var result = await db.MaintenanceResults.SingleOrDefaultAsync(result => result.Id == resultId && result.MaintenanceTaskId == taskId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy kết quả thực hiện của công việc.");

        var now = DateTimeOffset.UtcNow;
        var decision = command.Decision.Trim().ToLowerInvariant();
        if (decision == "approve")
        {
            if (result.ResultStatus == MaintenanceResultStatus.SUBMITTED)
                result.Approve(actor, now, command.ReviewNote);
            else if (result.ResultStatus != MaintenanceResultStatus.APPROVED)
                throw new InvalidOperationException("Kết quả cần được gửi để Manager phê duyệt.");

            task.Close(actor, now);
            if (task.Schedule is { Status: MaintenanceScheduleStatus.ACTIVE } schedule)
            {
                schedule.Complete(actor, now);
                await assets.MarkActiveAsync(schedule.FacilityId, schedule.EquipmentId, actor, ct);
            }
            db.MaintenanceTaskActivities.Add(new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.MANAGER_REVIEWED, now,
                fromStatus: MaintenanceTaskStatus.COMPLETED, toStatus: MaintenanceTaskStatus.COMPLETED, detail: command.ReviewNote, performedBy: actor));
            db.MaintenanceTaskActivities.Add(new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.CLOSED, now,
                fromStatus: MaintenanceTaskStatus.COMPLETED, toStatus: MaintenanceTaskStatus.CLOSED, performedBy: actor));
        }
        else if (decision == "request-revision")
        {
            result.RequestRevision(actor, now, command.ReviewNote);
            task.ReopenForFurtherWork(now);
            var teamAssignments = task.Assignments.Where(item => item.Status is AssignmentStatus.COMPLETED or AssignmentStatus.ASSIGNED or AssignmentStatus.IN_PROGRESS).ToArray();
            if (teamAssignments.Length == 0)
                throw new InvalidOperationException("Không tìm thấy nhóm nhân viên đã thực hiện để yêu cầu xử lý thêm.");
            foreach (var assignment in teamAssignments.Where(item => item.Status == AssignmentStatus.COMPLETED))
                assignment.ReopenForFurtherWork(now);
            db.MaintenanceTaskActivities.Add(new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.MANAGER_REVIEWED, now,
                fromStatus: MaintenanceTaskStatus.COMPLETED, toStatus: MaintenanceTaskStatus.ASSIGNED, detail: command.ReviewNote, performedBy: actor));
        }
        else
        {
            throw new ArgumentException("Quyết định review không hợp lệ.", nameof(command.Decision));
        }

        await db.SaveChangesAsync(ct);
        await NotifyVisibleAssigneesAsync(task, ct);
        return await ToTaskDtoAsync(task, task.Schedule, await MonitorAsync(task, ct), await ToDtoAsync(result, ct), ct);
    }

    public async Task<MaintenanceTaskDto> AssignTaskAsync(Guid taskId, AssignMaintenanceTaskCommand command, Guid actor, CancellationToken ct)
    {
        var requestedStaffIds = (command.StaffUserIds ?? [])
            .Append(command.StaffUserId ?? Guid.Empty)
            .Where(userId => userId != Guid.Empty)
            .Distinct()
            .ToArray();
        if (requestedStaffIds.Length == 0)
            throw new ArgumentException("Vui lòng chọn ít nhất một nhân viên xử lý.", nameof(command.StaffUserIds));

        var staffById = (await staffDirectory.GetActiveStaffAsync(ct)).ToDictionary(staff => staff.UserId);
        if (requestedStaffIds.Any(userId => !staffById.ContainsKey(userId)))
            throw new ArgumentException("Nhân viên xử lý không hợp lệ hoặc không còn hoạt động.", nameof(command.StaffUserIds));

        var task = await db.MaintenanceTasks.Include(task => task.Schedule).Include(task => task.Assignments)
            .SingleOrDefaultAsync(task => task.Id == taskId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy công việc bảo trì.");
        if (task.Status is not (MaintenanceTaskStatus.OPEN or MaintenanceTaskStatus.ASSIGNED or MaintenanceTaskStatus.IN_PROGRESS))
            throw new InvalidOperationException("Chỉ có thể phân công lại công việc đang mở hoặc đang xử lý.");
        var now = DateTimeOffset.UtcNow;
        var activeAssignments = task.Assignments.Where(assignment => assignment.IsActive).ToArray();
        var activeStaffIds = activeAssignments.Select(assignment => assignment.StaffUserId).ToHashSet();
        var assignmentsToRemove = activeAssignments.Where(assignment => !requestedStaffIds.Contains(assignment.StaffUserId)).ToArray();
        var staffIdsToAdd = requestedStaffIds.Where(userId => !activeStaffIds.Contains(userId)).ToArray();
        if (assignmentsToRemove.Length == 0 && staffIdsToAdd.Length == 0)
            throw new InvalidOperationException("Nhóm nhân viên xử lý không thay đổi.");

        foreach (var assignment in assignmentsToRemove)
            assignment.Reassign(now);

        var newAssignments = staffIdsToAdd
            .Select(staffUserId => new MaintenanceAssignment(task.Id, staffUserId, actor, now))
            .ToArray();
        db.MaintenanceAssignments.AddRange(newAssignments);

        var previousStatus = task.Status;
        if (task.Status == MaintenanceTaskStatus.OPEN)
            task.MarkAssigned(now);

        if (assignmentsToRemove.Length != 0 || newAssignments.Length != 0 || previousStatus != task.Status)
        {
            db.MaintenanceTaskActivities.Add(new MaintenanceTaskActivity(
                task.Id,
                previousStatus == MaintenanceTaskStatus.OPEN ? MaintenanceActivityType.ASSIGNED : MaintenanceActivityType.REASSIGNED,
                now,
                newAssignments.FirstOrDefault()?.Id,
                previousStatus,
                task.Status,
                performedBy: actor));
        }

        await db.SaveChangesAsync(ct);
        await notifier.NotifyTaskUpdatedAsync(taskId, activeStaffIds.Union(requestedStaffIds).ToArray(), ct);
        return await ToTaskDtoAsync(task, task.Schedule, await MonitorAsync(task, ct), await LatestResultAsync(task.Id, ct), ct);
    }

    public async Task<MaintenanceTaskDto> AssociateTaskAssetAsync(Guid taskId, AssociateMaintenanceTaskAssetCommand command, CancellationToken ct)
    {
        if (command.FacilityId is null && command.EquipmentId is null)
            throw new ArgumentException("Phải chọn cơ sở vật chất hoặc thiết bị.");

        await EnsureAssetsAsync(command.FacilityId, command.EquipmentId, ct, requireAnyAsset: true);

        var task = await db.MaintenanceTasks.Include(item => item.Assignments)
            .SingleOrDefaultAsync(item => item.Id == taskId, ct) ?? throw new KeyNotFoundException("Không tìm thấy công việc bảo trì.");
        task.SetAssetContext(command.FacilityId, command.EquipmentId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        await NotifyVisibleAssigneesAsync(task, ct);
        return ToDto(task);
    }
    public async Task<Page<ScheduleDto>> SchedulesAsync(ScheduleQuery query, CancellationToken ct)
    {
        EnsureValidRange(query.From, query.To);
        var source = db.MaintenanceSchedules.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.SearchKeyword)) { var key = query.SearchKeyword.Trim().ToLower(); source = source.Where(x => x.ScheduleCode.ToLower().Contains(key) || x.Title.ToLower().Contains(key)); }
        if (query.FacilityId.HasValue) source = source.Where(x => x.FacilityId == query.FacilityId);
        if (query.EquipmentId.HasValue) source = source.Where(x => x.EquipmentId == query.EquipmentId);
        if (query.Status.HasValue) source = source.Where(x => x.Status == query.Status);
        if (query.From.HasValue) source = source.Where(x => x.PlannedStartAt >= query.From);
        if (query.To.HasValue) source = source.Where(x => x.PlannedStartAt <= query.To);
        var total = await source.CountAsync(ct); var page = Math.Max(1, query.PageIndex); var size = Math.Clamp(query.PageSize, 1, 100);
        var items = await source.OrderByDescending(x => x.PlannedStartAt).Skip((page - 1) * size).Take(size).Select(x => ToDto(x)).ToListAsync(ct);
        return new(items, total, page, size);
    }

    public async Task<ScheduleDto> CreateScheduleAsync(CreateScheduleCommand command, Guid actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Description)) throw new ArgumentException("Mô tả là bắt buộc.");
        await EnsureAssetsAsync(command.FacilityId, command.EquipmentId, ct, requireAnyAsset: true);
        var codeObject = await AssetCodeObjectAsync(command.FacilityId, command.EquipmentId, allowCommon: false, ct);
        var startAt = Utc(command.PlannedStartAt);
        var codeDate = await BuildingDateAsync(startAt, ct);
        var baseCode = $"LBT-{codeObject}-{codeDate:ddMMyy}";
        MaintenanceSchedule? item = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            MaintenanceSchedule? candidate = null;
            try
            {
                item = await WithAdvisoryLockAsync($"schedule:{baseCode}", async token =>
                {
                    var codes = await db.MaintenanceSchedules.AsNoTracking().Where(existing => existing.ScheduleCode.StartsWith(baseCode)).Select(existing => existing.ScheduleCode).ToArrayAsync(token);
                    var scheduleCode = NextScheduleCode(codes, baseCode);
                    EnsureGeneratedCodeLength(scheduleCode, "Mã lịch bảo trì");
                    var created = new MaintenanceSchedule(scheduleCode, command.Title, startAt, actor, timeProvider.GetUtcNow(), command.FacilityId, command.EquipmentId, command.Description, Utc(command.PlannedEndAt));
                    candidate = created;
                    db.MaintenanceSchedules.Add(created);
                    await db.SaveChangesAsync(token);
                    return created;
                }, ct);
                break;
            }
            catch (DbUpdateException exception) when (candidate is not null && attempt < 2 && IsUniqueViolation(exception))
            {
                db.Entry(candidate).State = EntityState.Detached;
            }
        }
        if (item is null)
            throw new InvalidOperationException("Không thể cấp mã lịch bảo trì duy nhất. Vui lòng thử lại.");
        return ToDto(item);
    }
    public async Task<ScheduleDto> UpdateScheduleAsync(Guid id, UpdateScheduleCommand command, Guid actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Description)) throw new ArgumentException("Mô tả là bắt buộc.");
        await EnsureAssetsAsync(command.FacilityId, command.EquipmentId, ct, requireAnyAsset: true);
        var item = await db.MaintenanceSchedules.FindAsync([id], ct) ?? throw new KeyNotFoundException("Không tìm thấy lịch bảo trì.");
        item.UpdatePlan(command.Title, Utc(command.PlannedStartAt), actor, DateTimeOffset.UtcNow, command.FacilityId, command.EquipmentId, command.Description, Utc(command.PlannedEndAt));
        var eligible = await db.MaintenanceTasks.Where(task => task.ScheduleId == id && (task.Status == MaintenanceTaskStatus.OPEN || task.Status == MaintenanceTaskStatus.ASSIGNED)).ToListAsync(ct);
        foreach (var task in eligible) task.UpdateDetails(task.Title, DateTimeOffset.UtcNow, command.Description, task.PriorityCode, task.PlannedStartAt, task.DueAt);
        await db.SaveChangesAsync(ct); return ToDto(item);
    }
    public async Task<ScheduleDto> SetScheduleStatusAsync(Guid id, string action, Guid actor, CancellationToken ct)
    {
        var item = await db.MaintenanceSchedules.FindAsync([id], ct) ?? throw new KeyNotFoundException("Không tìm thấy lịch bảo trì.");
        if (string.Equals(action, "complete", StringComparison.OrdinalIgnoreCase))
        {
            item.Complete(actor, DateTimeOffset.UtcNow);
            await assets.MarkActiveAsync(item.FacilityId, item.EquipmentId, actor, ct);
        }
        else if (string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            var now = DateTimeOffset.UtcNow;
            item.Cancel(actor, now);
            var tasks = await db.MaintenanceTasks.Include(task => task.Assignments)
                .Where(task => task.ScheduleId == id && task.Status != MaintenanceTaskStatus.CLOSED && task.Status != MaintenanceTaskStatus.CANCELLED)
                .ToListAsync(ct);
            foreach (var task in tasks)
            {
                var previousStatus = task.Status;
                task.Cancel(now);
                foreach (var assignment in task.Assignments.Where(assignment => assignment.IsActive))
                    assignment.Cancel(now);
                db.MaintenanceTaskActivities.Add(new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.STATUS_CHANGED, now,
                    fromStatus: previousStatus, toStatus: MaintenanceTaskStatus.CANCELLED,
                    detail: "Công việc được hủy vì lịch bảo trì nguồn đã bị hủy.", performedBy: actor));
            }
            await assets.MarkActiveAsync(item.FacilityId, item.EquipmentId, actor, ct);
        }
        else throw new ArgumentException("Thao tác lịch bảo trì không hợp lệ.");
        await db.SaveChangesAsync(ct); return ToDto(item);
    }

    public async Task<ScheduleDto> ExtendScheduleAsync(Guid id, ExtendScheduleCommand command, Guid actor, CancellationToken ct)
    {
        var item = await db.MaintenanceSchedules.FindAsync([id], ct) ?? throw new KeyNotFoundException("Không tìm thấy lịch bảo trì.");
        var now = DateTimeOffset.UtcNow;
        if (item.PlannedStartAt > now)
            throw new InvalidOperationException("Chỉ có thể gia hạn khi lịch bảo trì đã bắt đầu.");

        var duration = command.Unit.Trim().ToLowerInvariant() switch
        {
            "minute" => TimeSpan.FromMinutes(command.Amount),
            "hour" => TimeSpan.FromHours(command.Amount),
            "day" => TimeSpan.FromDays(command.Amount),
            _ => throw new ArgumentException("Đơn vị gia hạn không hợp lệ.", nameof(command.Unit))
        };
        item.Extend(duration, actor, now);
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task ActivateDueSchedulesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var dueSchedules = await db.MaintenanceSchedules.AsNoTracking()
            .Where(schedule => schedule.Status == MaintenanceScheduleStatus.ACTIVE && schedule.PlannedStartAt <= now)
            .ToListAsync(ct);

        foreach (var schedule in dueSchedules)
        {
            var facility = schedule.FacilityId is { } facilityId ? await assets.GetFacilityAsync(facilityId, ct) : null;
            var equipment = schedule.EquipmentId is { } equipmentId ? await assets.GetEquipmentAsync(equipmentId, ct) : null;
            if ((facility is null && equipment is null) || (facility is not null && equipment?.FacilityId is { } equipmentFacilityId && equipmentFacilityId != facility.Id))
                continue;
            // The activated scope follows the same rule as MaintenanceAssetSource:
            // an equipment selection is the only affected asset; otherwise the facility is the marker
            // for a facility-wide activation. This prevents the 10-second worker from re-notifying.
            var isAlreadyActivated = equipment is not null
                ? equipment.Status == "UNDER_MAINTENANCE"
                : facility?.Status == "UNDER_MAINTENANCE";
            if (isAlreadyActivated)
                continue;

            await assets.MarkUnderMaintenanceAsync(schedule.FacilityId, schedule.EquipmentId, schedule.CreatedBy, ct);
            await NotifyAssetStatusChangedAfterCommitAsync(ct);
        }
    }

    public Task<IReadOnlyList<MaintenanceAssetReference>> AssetsAsync(CancellationToken ct) => assets.GetAvailableAsync(ct);
    public async Task<IReadOnlyList<AssignableMaintenanceStaffDto>> AssignableStaffAsync(CancellationToken ct) =>
        (await staffDirectory.GetActiveStaffAsync(ct))
            .Select(staff => new AssignableMaintenanceStaffDto(staff.UserId, staff.Username, staff.DisplayName))
            .ToArray();
    public async Task<IReadOnlyList<AssignableMaintenanceStaffDto>> HistoryStaffAsync(CancellationToken ct)
    {
        var ids = await db.MaintenanceAssignments.AsNoTracking().Select(assignment => assignment.StaffUserId).Distinct().ToArrayAsync(ct);
        return (await staffDirectory.GetHistoricalStaffAsync(ids, ct))
            .OrderBy(staff => staff.DisplayName).ThenBy(staff => staff.Username)
            .Select(staff => new AssignableMaintenanceStaffDto(staff.UserId, staff.Username, staff.DisplayName))
            .ToArray();
    }
    private async Task EnsureAssetsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct, bool requireAnyAsset = false, bool requireUnderMaintenance = false, bool requireFacility = false)
    {
        if (facilityId is null && equipmentId is null)
        {
            if (requireAnyAsset) throw new ArgumentException("Phải chọn cơ sở vật chất hoặc thiết bị.");
            return;
        }

        if (requireFacility && facilityId is null)
            throw new ArgumentException("Phải chọn cơ sở vật chất.");

        var facility = facilityId is { } selectedFacilityId
            ? await assets.GetFacilityAsync(selectedFacilityId, ct) ?? throw new KeyNotFoundException("Không tìm thấy cơ sở vật chất.")
            : null;
        var equipment = equipmentId is { } selectedEquipmentId
            ? await assets.GetEquipmentAsync(selectedEquipmentId, ct) ?? throw new KeyNotFoundException("Không tìm thấy thiết bị.")
            : null;

        if (facility is not null && equipment?.FacilityId is not null && equipment.FacilityId != facility.Id)
            throw new ArgumentException("Thiết bị không thuộc cơ sở vật chất đã chọn.");
        if (requireUnderMaintenance && facility is not null && !string.Equals(facility.Status, "UNDER_MAINTENANCE", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Chỉ được chọn cơ sở vật chất đang bảo trì.");
        if (requireUnderMaintenance && equipment is not null && !string.Equals(equipment.Status, "UNDER_MAINTENANCE", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Chỉ được chọn thiết bị đang bảo trì.");
    }

    private async Task EnsureEmergencyAssetsCanEnterMaintenanceAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct)
    {
        var facility = facilityId is { } selectedFacilityId
            ? await assets.GetFacilityAsync(selectedFacilityId, ct)
            : null;
        var equipment = equipmentId is { } selectedEquipmentId
            ? await assets.GetEquipmentAsync(selectedEquipmentId, ct)
            : null;

        if (facility is not null && facility.Status is not ("ACTIVE" or "UNDER_MAINTENANCE"))
            throw new InvalidOperationException("Cơ sở vật chất không ở trạng thái có thể chuyển sang đang bảo trì.");
        if (equipment is not null && equipment.Status is not ("ACTIVE" or "UNDER_MAINTENANCE"))
            throw new InvalidOperationException("Thiết bị không ở trạng thái có thể chuyển sang đang bảo trì.");
    }
    private static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
    private static DateTimeOffset? Utc(DateTimeOffset? value) => value?.ToUniversalTime();

    private async Task<string> AssetCodeObjectAsync(Guid? facilityId, Guid? equipmentId, bool allowCommon, CancellationToken ct)
    {
        var facility = facilityId is { } facilityValue ? await assets.GetFacilityAsync(facilityValue, ct) : null;
        var equipment = equipmentId is { } equipmentValue ? await assets.GetEquipmentAsync(equipmentValue, ct) : null;
        var values = new List<string>(2);
        if (facility is not null)
            values.Add(IdentifierCodeNormalizer.NormalizeRequired(facility.FacilityType ?? facility.Name, "Loại cơ sở vật chất"));
        if (equipment is not null)
            values.Add(IdentifierCodeNormalizer.NormalizeRequired(equipment.Name, "Tên thiết bị"));
        if (values.Count == 0)
        {
            if (allowCommon) return "CHUNG";
            throw new ArgumentException("Phải chọn cơ sở vật chất hoặc thiết bị.");
        }
        return string.Join('-', values);
    }

    private async Task<DateOnly> BuildingDateAsync(DateTimeOffset instant, CancellationToken ct)
    {
        var timeZoneId = buildingTimeZone is null ? "Asia/Ho_Chi_Minh" : await buildingTimeZone.GetAsync(ct);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    private async Task<T> WithAdvisoryLockAsync<T>(string scope, Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return await action(ct);
        if (db.Database.CurrentTransaction is not null)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [IdentifierCodeNormalizer.AdvisoryLockKey(scope)], ct);
            return await action(ct);
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [IdentifierCodeNormalizer.AdvisoryLockKey(scope)], ct);
        var result = await action(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private static int NextSequence(IReadOnlyList<string> codes, string prefix)
    {
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code[prefix.Length..], out var sequence)) max = Math.Max(max, sequence);
        return max + 1;
    }

    private static string NextScheduleCode(IReadOnlyList<string> codes, string baseCode)
    {
        if (!codes.Contains(baseCode, StringComparer.Ordinal)) return baseCode;
        var max = 1;
        foreach (var code in codes)
            if (code.StartsWith(baseCode + "-", StringComparison.Ordinal) && int.TryParse(code[(baseCode.Length + 1)..], out var sequence))
                max = Math.Max(max, sequence);
        return $"{baseCode}-{max + 1:000}";
    }

    private static void EnsureGeneratedCodeLength(string code, string name)
    {
        if (code.Length > 512) throw new ArgumentException($"{name} sau chuẩn hóa vượt quá 512 ký tự.");
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private static void EnsureValidRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from.HasValue && to.HasValue && from > to)
            throw new ArgumentException("Từ ngày không được sau đến ngày.");
    }
    private static ScheduleDto ToDto(MaintenanceSchedule x) => new(x.Id, x.ScheduleCode, x.FacilityId, x.EquipmentId, x.Title, x.Description, x.PlannedStartAt, x.PlannedEndAt, x.Status);
    private async Task<MaintenanceTaskDto> ToTaskDtoAsync(MaintenanceTask task, MaintenanceSchedule? schedule, CancellationToken ct) =>
        await ToTaskDtoAsync(task, schedule, TaskMonitor.For(task, timeProvider.GetUtcNow()), null, ct);

    private async Task<MaintenanceTaskDto> ToTaskDtoAsync(MaintenanceTask task, MaintenanceSchedule? schedule, TaskMonitor monitor, MaintenanceResultDto? latestResult, CancellationToken ct)
    {
        var staffById = (await staffDirectory.GetActiveStaffAsync(ct)).ToDictionary(staff => staff.UserId);
        var related = task.SourceServiceRequestId is { } sourceId && serviceRequestSource is not null
            ? await ToRelatedServiceRequestAsync(task, await serviceRequestSource.GetAsync(sourceId, ct), ct)
            : null;
        return ToDto(task, schedule, CurrentAssignees(task, staffById), monitor, latestResult, related);
    }

    private async Task<MaintenanceResultDto?> LatestResultAsync(Guid taskId, CancellationToken ct)
    {
        var result = await db.MaintenanceResults.AsNoTracking().Where(result => result.MaintenanceTaskId == taskId)
            .OrderByDescending(result => result.AttemptNo).FirstOrDefaultAsync(ct);
        return result is null ? null : await ToDtoAsync(result, ct);
    }

    private async Task<TaskMonitor> MonitorAsync(MaintenanceTask task, CancellationToken ct)
    {
        var activities = db.MaintenanceTaskActivities.AsNoTracking().Where(activity => activity.MaintenanceTaskId == task.Id);
        var lastActivity = await activities.OrderByDescending(activity => activity.CreatedAt)
            .Select(activity => new ActivitySnapshot(activity.CreatedAt, activity.ActivityType, activity.Detail)).FirstOrDefaultAsync(ct);
        var lastProgress = await activities
            .Where(activity => activity.ActivityType == MaintenanceActivityType.STATUS_CHANGED ||
                               activity.ActivityType == MaintenanceActivityType.PROGRESS_UPDATED ||
                               activity.ActivityType == MaintenanceActivityType.WORK_LOG_ADDED ||
                               activity.ActivityType == MaintenanceActivityType.COMPLETED)
            .OrderByDescending(activity => activity.CreatedAt)
            .Select(activity => new ActivitySnapshot(activity.CreatedAt, activity.ActivityType, activity.Detail)).FirstOrDefaultAsync(ct);
        return TaskMonitor.For(task, timeProvider.GetUtcNow(), lastActivity, lastProgress);
    }

    private MaintenanceTaskDto ToDto(
        MaintenanceTask x,
        MaintenanceSchedule? schedule = null,
        IReadOnlyList<AssignableMaintenanceStaffDto>? currentAssignees = null,
        TaskMonitor? monitor = null,
        MaintenanceResultDto? latestResult = null,
        RelatedServiceRequestDto? relatedServiceRequest = null) => new(
            x.Id, x.TaskNumber, x.ScheduleId, x.FacilityId, x.EquipmentId, x.Title, x.Description, x.PriorityCode,
            x.Status, x.PlannedStartAt, x.DueAt, x.CreatedBy, x.CreatedAt, schedule?.ScheduleCode, schedule?.Title,
            currentAssignees?.FirstOrDefault()?.UserId, currentAssignees?.FirstOrDefault()?.DisplayName, currentAssignees?.FirstOrDefault()?.Username,
            x.Assignments.Where(assignment => assignment.IsActive).OrderBy(assignment => assignment.AssignedAt).Select(assignment => (DateTimeOffset?)assignment.AssignedAt).FirstOrDefault(),
            monitor?.LastActivity?.At, monitor?.LastActivity?.Type, monitor?.LastProgress?.At, monitor?.LastProgress?.Type, monitor?.LastProgress?.Detail,
            monitor?.IsOverdue ?? IsTaskOverdue(x), latestResult, currentAssignees ?? [], x.SourceServiceRequestId, relatedServiceRequest);

    private async Task<ServiceRequestMaintenanceReference> GetLinkableServiceRequestAsync(Guid serviceRequestId, CancellationToken ct)
    {
        var source = serviceRequestSource ?? throw new InvalidOperationException("Dịch vụ yêu cầu liên quan chưa sẵn sàng.");
        var request = await source.GetAsync(serviceRequestId, ct)
            ?? throw new ArgumentException("Không tìm thấy yêu cầu dịch vụ liên quan.", nameof(serviceRequestId));
        if (!request.IsLinkable)
            throw new InvalidOperationException("Yêu cầu dịch vụ hiện không đủ điều kiện để liên kết công việc bảo trì.");
        return request;
    }

    private async Task<RelatedServiceRequestDto?> ToRelatedServiceRequestAsync(
        MaintenanceTask task,
        ServiceRequestMaintenanceReference? request,
        CancellationToken ct)
    {
        if (request is null)
            return null;

        var taskContext = await ResolveAssetContextAsync(task.FacilityId, task.EquipmentId, ct);
        var requestContext = await ResolveAssetContextAsync(request.FacilityId, request.EquipmentId, ct);
        var comparison = CompareAssetContexts(taskContext, requestContext);
        var location = DescribeRequestLocation(request, requestContext);

        return new(
            request.Id,
            request.RequestNumber,
            request.Title,
            request.Status,
            request.ApartmentUnitId,
            request.FacilityId,
            request.EquipmentId,
            request.PriorityCode,
            location,
            comparison.Match,
            comparison.Warning);
    }

    private async Task<AssetContext> ResolveAssetContextAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct)
    {
        var facility = facilityId is { } directFacilityId
            ? await assets.GetFacilityAsync(directFacilityId, ct)
            : null;
        var equipment = equipmentId is { } equipmentIdValue
            ? await assets.GetEquipmentAsync(equipmentIdValue, ct)
            : null;

        var effectiveFacilityId = facilityId ?? equipment?.FacilityId;
        if (facility is null && effectiveFacilityId is { } inferredFacilityId)
            facility = await assets.GetFacilityAsync(inferredFacilityId, ct);

        return new AssetContext(
            effectiveFacilityId,
            equipmentId,
            facility?.Code,
            facility?.Name,
            equipment?.Code,
            equipment?.Name);
    }

    private static ContextComparison CompareAssetContexts(AssetContext task, AssetContext request)
    {
        // Equipment is the most precise context. A facility match through an equipment's
        // parent facility is still meaningful; otherwise no missing value is treated as a match.
        if (task.EquipmentId is { } taskEquipment && request.EquipmentId is { } requestEquipment)
            return taskEquipment == requestEquipment
                ? new("MATCHED", null)
                : DifferentContext();

        if (task.FacilityId is { } taskFacility && request.FacilityId is { } requestFacility)
            return taskFacility == requestFacility
                ? new("MATCHED", null)
                : DifferentContext();

        return new("INSUFFICIENT_DATA", "Chưa đủ dữ liệu để xác định sự phù hợp giữa yêu cầu dịch vụ và công việc bảo trì.");
    }

    private static ContextComparison DifferentContext() => new(
        "DIFFERENT_CONTEXT",
        "Vị trí hoặc tài sản của yêu cầu dịch vụ khác với công việc bảo trì. Vui lòng kiểm tra và giải thích lý do liên kết.");

    private static string? DescribeRequestLocation(ServiceRequestMaintenanceReference request, AssetContext context)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ApartmentUnitNumber))
        {
            var apartment = $"Căn hộ thụ hưởng: {request.ApartmentUnitNumber}";
            if (request.ApartmentFloorNumber is { } floorNumber)
                apartment += $" · Tầng {floorNumber}";
            parts.Add(apartment);
        }
        else if (request.ApartmentUnitId is not null)
        {
            parts.Add("Căn hộ thụ hưởng: Chưa xác định");
        }
        if (!string.IsNullOrWhiteSpace(context.FacilityName))
            parts.Add($"Cơ sở vật chất: {context.FacilityCode} · {context.FacilityName}");
        if (!string.IsNullOrWhiteSpace(context.EquipmentName))
            parts.Add($"Thiết bị: {context.EquipmentCode} · {context.EquipmentName}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static MaintenanceTaskActivity ServiceRequestLinkedActivity(Guid taskId, ServiceRequestMaintenanceReference request, Guid actor, DateTimeOffset now) =>
        new(taskId, MaintenanceActivityType.SERVICE_REQUEST_LINKED, now,
            detail: $"Đã liên kết yêu cầu dịch vụ {request.RequestNumber} ({request.Id}).", performedBy: actor);

    private static IReadOnlyList<AssignableMaintenanceStaffDto> CurrentAssignees(
        MaintenanceTask task,
        IReadOnlyDictionary<Guid, MaintenanceStaffRecord> staffById)
    {
        // Terminal tasks no longer have active assignments because completion closes the
        // whole team. Keep their last effective team in the read model so Manager and
        // Staff do not see the misleading "Chưa phân công" label.
        var assignments = task.Assignments.Where(assignment => assignment.IsActive).ToArray();
        if (assignments.Length == 0 && task.Status is MaintenanceTaskStatus.COMPLETED or MaintenanceTaskStatus.CLOSED)
            assignments = task.Assignments.Where(assignment => assignment.Status is AssignmentStatus.COMPLETED or AssignmentStatus.ASSIGNED or AssignmentStatus.IN_PROGRESS).ToArray();

        return assignments
            .OrderBy(assignment => assignment.AssignedAt)
            .ThenBy(assignment => assignment.Id)
            .Select(assignment => staffById.TryGetValue(assignment.StaffUserId, out var staff)
                ? new AssignableMaintenanceStaffDto(staff.UserId, staff.Username, staff.DisplayName)
                : new AssignableMaintenanceStaffDto(assignment.StaffUserId, string.Empty, "Nhân viên không còn hoạt động"))
            .ToArray();
    }

    private static MaintenanceResultDto ToDto(MaintenanceResult result) => new(
        result.Id, result.AttemptNo, result.SubmittedBy, result.Summary, result.WorkPerformed, result.IssueFound,
        result.PartsOrResourcesUsed, result.Recommendation, result.ResultStatus, result.ReviewedBy, result.ReviewNote,
        result.SubmittedAt, result.ReviewedAt);

    private async Task<MaintenanceResultDto> ToDtoAsync(MaintenanceResult result, CancellationToken ct)
    {
        var attachments = await db.MaintenanceResultAttachments.AsNoTracking()
            .Where(item => item.MaintenanceResultId == result.Id).OrderBy(item => item.UploadedAt).ThenBy(item => item.Id)
            .Select(item => new MaintenanceResultAttachmentDto(item.Id, item.OriginalFileName, item.ContentType, item.SizeBytes)).ToListAsync(ct);
        return ToDto(result) with { Attachments = attachments };
    }

    private async Task<IReadOnlyDictionary<Guid, MaintenanceStaffRecord>> HistoricalStaffByIdAsync(IEnumerable<Guid> userIds, CancellationToken ct) =>
        (await staffDirectory.GetHistoricalStaffAsync(userIds.Where(id => id != Guid.Empty).Distinct().ToArray(), ct))
            .ToDictionary(staff => staff.UserId);

    private static MaintenanceHistoryItemDto ToHistoryItem(MaintenanceTask task, DateTimeOffset historyAt, MaintenanceResult? latestResult, IReadOnlyDictionary<Guid, MaintenanceStaffRecord> staffById)
    {
        var participants = task.Assignments.OrderBy(assignment => assignment.AssignedAt).ThenBy(assignment => assignment.Id)
            .Select(assignment => assignment.StaffUserId).Distinct()
            .Select(userId => staffById.TryGetValue(userId, out var staff)
                ? new HistoryParticipantDto(userId, staff.Username, staff.DisplayName)
                : new HistoryParticipantDto(userId, null, null))
            .ToArray();
        return new(task.Id, task.TaskNumber, task.Title, task.Description, task.PriorityCode, task.Status, task.FacilityId, task.EquipmentId,
            task.Schedule?.ScheduleCode, task.Schedule?.Title, task.PlannedStartAt, task.DueAt, task.StartedAt, task.CompletedAt, task.ClosedAt, historyAt, participants,
            latestResult?.Summary, latestResult?.ResultStatus);
    }

    private static MaintenanceAssignmentHistoryDto ToHistoryAssignment(MaintenanceAssignment assignment, MaintenanceStaffRecord? staff) => new(
        assignment.Id, assignment.StaffUserId, staff?.Username, staff?.DisplayName, assignment.Status,
        assignment.AssignedAt, assignment.StartedAt, assignment.CompletedAt, assignment.EndedAt, assignment.AssignmentNote);

    private static IReadOnlyList<MaintenanceHistoryTimelineEventDto> BuildTimeline(MaintenanceTask task, IReadOnlyList<MaintenanceTaskActivity> activities, IReadOnlyList<MaintenanceResult> results)
    {
        var timeline = new List<MaintenanceHistoryTimelineEventDto>
        {
            new(task.Id, task.CreatedAt, "TASK_CREATED", Detail: null, PerformedBy: task.CreatedBy)
        };
        timeline.AddRange(activities.Select(activity => new MaintenanceHistoryTimelineEventDto(activity.Id, activity.CreatedAt, "ACTIVITY",
            activity.ActivityType, activity.FromStatus, activity.ToStatus, activity.Detail, activity.PerformedBy)));
        timeline.AddRange(results
            .Where(result => !activities.Any(activity => activity.ActivityType == MaintenanceActivityType.RESULT_SUBMITTED && activity.CreatedAt == result.SubmittedAt))
            .Select(result => new MaintenanceHistoryTimelineEventDto(result.Id, result.SubmittedAt, "RESULT_SUBMITTED", Detail: result.Summary, PerformedBy: result.SubmittedBy)));
        return timeline.OrderBy(item => item.OccurredAt).ThenBy(item => TimelineOrder(item.Kind)).ThenBy(item => item.Id).ToArray();
    }

    private static int TimelineOrder(string kind) => kind switch
    {
        "TASK_CREATED" => 0,
        "ACTIVITY" => 1,
        "RESULT_SUBMITTED" => 2,
        _ => 9
    };

    private bool IsTaskOverdue(MaintenanceTask task) => IsTaskOverdue(task, timeProvider.GetUtcNow());
    public static bool IsTaskOverdue(MaintenanceTask task, DateTimeOffset now) => task.DueAt is { } due && now > due &&
        task.Status is not (MaintenanceTaskStatus.COMPLETED or MaintenanceTaskStatus.CLOSED or MaintenanceTaskStatus.CANCELLED);

    private sealed record ActivitySnapshot(DateTimeOffset At, MaintenanceActivityType Type, string? Detail = null);
    private sealed record AssetContext(Guid? FacilityId, Guid? EquipmentId, string? FacilityCode, string? FacilityName, string? EquipmentCode, string? EquipmentName);
    private sealed record ContextComparison(string Match, string? Warning);
    private sealed record TaskMonitor(ActivitySnapshot? LastActivity, ActivitySnapshot? LastProgress, bool IsOverdue)
    {
        public static TaskMonitor For(MaintenanceTask task, DateTimeOffset now, ActivitySnapshot? lastActivity = null, ActivitySnapshot? lastProgress = null) =>
            new(lastActivity, lastProgress, MaintenanceService.IsTaskOverdue(task, now));
    }

    private Task NotifyVisibleAssigneesAsync(MaintenanceTask task, CancellationToken ct) =>
        notifier.NotifyTaskUpdatedAsync(task.Id, task.Assignments
            .Where(assignment => assignment.Status is AssignmentStatus.ASSIGNED or AssignmentStatus.IN_PROGRESS or AssignmentStatus.COMPLETED)
            .Select(assignment => assignment.StaffUserId)
            .Distinct()
            .ToArray(), ct);

    private async Task NotifyAssetStatusChangedAfterCommitAsync(CancellationToken ct)
    {
        try
        {
            // MarkUnderMaintenanceAsync has already persisted the asset update before this notification.
            await notifier.NotifyAssetStatusChangedAsync(ct);
        }
        catch
        {
            // SignalR is a best-effort UI notification and must not make a committed schedule activation fail.
        }
    }

    private sealed class NoopMaintenanceNotifier : IMaintenanceNotifier
    {
        public static readonly NoopMaintenanceNotifier Instance = new();

        public Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientStaffUserIds, CancellationToken ct = default) => Task.CompletedTask;
    }
}
