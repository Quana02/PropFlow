using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestEvidence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class StaffServiceRequestStore(ServiceRequestsDbContext db) : IStaffServiceRequestStore
{
    public async Task<StaffServiceRequestPage> ListAsync(Guid staffUserId, StaffServiceRequestListQuery query, CancellationToken ct)
    {
        var requests = VisibleRequests(staffUserId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            requests = requests.Where(x => EF.Functions.ILike(x.RequestNumber, $"%{search}%"));
        }
        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<ServiceRequestStatus>(query.Status, out var status))
            requests = status == ServiceRequestStatus.RESOLVED
                ? requests.Where(x => x.Status == ServiceRequestStatus.RESOLVED || x.Status == ServiceRequestStatus.CLOSED)
                : requests.Where(x => x.Status == status);

        var totalCount = await requests.CountAsync(ct);
        var items = await requests.OrderByDescending(x => x.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new StaffServiceRequestCore(
                x.Id, x.RequestNumber, x.Title, x.Description, x.Category == null ? null : x.Category.Name,
                x.ApartmentUnitId, x.FacilityId, x.EquipmentId, x.Status.ToString(), x.FinalPriorityCode,
                x.ServiceAreaCode, x.PreferredDate, x.PreferredTimeCode,
                x.Assignments.Where(a => a.StaffUserId == staffUserId &&
                        (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS || a.Status == AssignmentStatus.COMPLETED))
                    .OrderByDescending(a => a.AssignedAt).Select(a => a.Id).First(),
                x.Assignments.Where(a => a.StaffUserId == staffUserId &&
                        (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS || a.Status == AssignmentStatus.COMPLETED))
                    .OrderByDescending(a => a.AssignedAt).Select(a => a.AssignedAt).First(),
                x.Assignments.Where(a => a.StaffUserId == staffUserId &&
                        (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS || a.Status == AssignmentStatus.COMPLETED))
                    .OrderByDescending(a => a.AssignedAt).Select(a => a.StartedAt).FirstOrDefault(),
                x.UpdatedAt))
            .ToArrayAsync(ct);

        var all = VisibleRequests(staffUserId);
        var summary = new StaffServiceRequestSummary(
            await all.CountAsync(ct),
            await all.CountAsync(x => x.Status == ServiceRequestStatus.ASSIGNED, ct),
            await all.CountAsync(x => x.Status == ServiceRequestStatus.IN_PROGRESS, ct),
            await all.CountAsync(x => x.Status == ServiceRequestStatus.RESOLVED || x.Status == ServiceRequestStatus.CLOSED, ct));
        return new(items, totalCount, summary);
    }

    public Task<StaffServiceRequestCore?> GetByNumberAsync(Guid staffUserId, string requestNumber, CancellationToken ct) =>
        ProjectCore(
                VisibleRequests(staffUserId).Where(x => EF.Functions.ILike(x.RequestNumber, requestNumber)),
                staffUserId)
            .SingleOrDefaultAsync(ct);

    public Task<StaffServiceRequestCore?> GetByIdAsync(Guid staffUserId, Guid id, CancellationToken ct) =>
        ProjectCore(VisibleRequests(staffUserId).Where(x => x.Id == id), staffUserId)
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<StaffServiceRequestActivityItem>> ActivitiesAsync(Guid staffUserId, Guid id, CancellationToken ct)
    {
        if (!await VisibleRequests(staffUserId).AnyAsync(x => x.Id == id, ct)) return [];
        return await db.ServiceRequestActivities.AsNoTracking().Where(x => x.ServiceRequestId == id)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new StaffServiceRequestActivityItem(x.Id, x.ActivityType.ToString(), x.Title, x.Detail,
                x.WorkResult, x.PerformedBy, x.CreatedAt)).ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<StaffEvidenceMetadata>> EvidenceAsync(Guid staffUserId, Guid id, CancellationToken ct)
    {
        if (!await VisibleRequests(staffUserId).AnyAsync(x => x.Id == id, ct)) return [];
        return await db.ServiceRequestEvidenceFiles.AsNoTracking().Where(x => x.ServiceRequestId == id)
            .OrderBy(x => x.UploadedAt).Select(x => new StaffEvidenceMetadata(
                x.Id, x.ServiceRequestId, x.ActivityId, x.FileName, x.ContentType, x.SizeBytes,
                x.StorageKey, x.UploadedBy, x.UploadedAt)).ToArrayAsync(ct);
    }

    public async Task<Guid?> PendingWorkResultActivityIdAsync(Guid staffUserId, Guid id, CancellationToken ct)
    {
        if (!await ActiveRequestAsync(staffUserId, id).AnyAsync(ct)) return null;
        var latestResult = await db.ServiceRequestActivities.AsNoTracking()
            .Where(x => x.ServiceRequestId == id && x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED && x.PerformedBy == staffUserId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new { x.Id, x.CreatedAt }).FirstOrDefaultAsync(ct);
        if (latestResult is null) return null;
        var newerRework = await db.ServiceRequestActivities.AsNoTracking().AnyAsync(x => x.ServiceRequestId == id &&
            x.ActivityType == ServiceActivityType.REWORK_REQUESTED && x.CreatedAt >= latestResult.CreatedAt, ct);
        if (newerRework) return null;
        var alreadyUploaded = await db.ServiceRequestEvidenceFiles.AsNoTracking().AnyAsync(x => x.ActivityId == latestResult.Id, ct);
        return alreadyUploaded ? null : latestResult.Id;
    }

    public async Task<StaffEvidenceMetadata?> EvidenceFileAsync(Guid staffUserId, Guid requestId, Guid evidenceId, CancellationToken ct)
    {
        if (!await VisibleRequests(staffUserId).AnyAsync(x => x.Id == requestId, ct)) return null;
        return await db.ServiceRequestEvidenceFiles.AsNoTracking()
            .Where(x => x.Id == evidenceId && x.ServiceRequestId == requestId)
            .Select(x => new StaffEvidenceMetadata(
                x.Id, x.ServiceRequestId, x.ActivityId, x.FileName, x.ContentType, x.SizeBytes,
                x.StorageKey, x.UploadedBy, x.UploadedAt)).SingleOrDefaultAsync(ct);
    }

    public async Task<StaffServiceRequestOutcome> AcceptAsync(Guid staffUserId, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var state = await CurrentAsync(staffUserId, id, ct);
        if (state.Request is null || state.Assignment is null) return StaffServiceRequestOutcome.NotFound;
        if (state.Request.Status != ServiceRequestStatus.ASSIGNED || state.Assignment.Status != AssignmentStatus.ASSIGNED)
            return StaffServiceRequestOutcome.Conflict;
        state.Assignment.Start(now);
        state.Request.MarkInProgress(now);
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(id, ServiceActivityType.STATUS_CHANGED, now,
            state.Assignment.Id, ServiceRequestStatus.ASSIGNED, ServiceRequestStatus.IN_PROGRESS,
            "Staff đã tiếp nhận yêu cầu", performedBy: staffUserId));
        return await SaveAsync(ct);
    }

    public async Task<StaffServiceRequestOutcome> AddProgressAsync(Guid staffUserId, Guid id, string detail, DateTimeOffset now, CancellationToken ct)
    {
        var state = await CurrentAsync(staffUserId, id, ct);
        if (state.Request is null || state.Assignment is null) return StaffServiceRequestOutcome.NotFound;
        if (state.Assignment.Status != AssignmentStatus.IN_PROGRESS || state.Request.Status != ServiceRequestStatus.IN_PROGRESS)
            return StaffServiceRequestOutcome.Conflict;
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(id, ServiceActivityType.PROGRESS_UPDATED, now,
            state.Assignment.Id, title: "Staff cập nhật tiến độ", detail: detail, performedBy: staffUserId));
        return await SaveAsync(ct);
    }

    public async Task<StaffServiceRequestOutcome> CompleteWorkAsync(Guid staffUserId, Guid id, string workResult, DateTimeOffset now, CancellationToken ct)
    {
        var state = await CurrentAsync(staffUserId, id, ct);
        if (state.Request is null || state.Assignment is null) return StaffServiceRequestOutcome.NotFound;
        if (state.Assignment.Status != AssignmentStatus.IN_PROGRESS || state.Request.Status != ServiceRequestStatus.IN_PROGRESS)
            return StaffServiceRequestOutcome.Conflict;
        var latestResult = state.Request.Activities.Where(x => x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault();
        var latestRework = state.Request.Activities.Where(x => x.ActivityType == ServiceActivityType.REWORK_REQUESTED)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefault();
        if (latestResult is not null && (latestRework is null || latestResult.CreatedAt > latestRework.CreatedAt))
            return StaffServiceRequestOutcome.Conflict;
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(id, ServiceActivityType.WORK_RESULT_ADDED, now,
            state.Assignment.Id, title: "Staff xác nhận hoàn thành công việc", workResult: workResult, performedBy: staffUserId));
        return await SaveAsync(ct);
    }

    public async Task<StaffServiceRequestOutcome> AddEvidenceAsync(Guid staffUserId, Guid id, Guid activityId,
        IReadOnlyList<StaffEvidenceMetadata> evidence, DateTimeOffset now, CancellationToken ct)
    {
        var state = await CurrentAsync(staffUserId, id, ct);
        if (state.Request is null || state.Assignment is null) return StaffServiceRequestOutcome.NotFound;
        if (state.Assignment.Status != AssignmentStatus.IN_PROGRESS || state.Request.Status != ServiceRequestStatus.IN_PROGRESS)
            return StaffServiceRequestOutcome.Conflict;
        var pendingId = await PendingWorkResultActivityIdAsync(staffUserId, id, ct);
        if (pendingId != activityId) return StaffServiceRequestOutcome.Conflict;
        db.ServiceRequestEvidenceFiles.AddRange(evidence.Select(x => new ServiceRequestEvidenceFile(
            x.ServiceRequestId, x.ActivityId, x.FileName, x.ContentType, x.SizeBytes, x.StorageKey, x.UploadedBy, x.UploadedAt)));
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(id, ServiceActivityType.PROGRESS_UPDATED, now,
            state.Assignment.Id, title: "Staff đã tải ảnh bằng chứng", detail: $"Đã tải {evidence.Count} ảnh bằng chứng hoàn thành.", performedBy: staffUserId));
        return await SaveAsync(ct);
    }

    private IQueryable<ServiceRequest> VisibleRequests(Guid staffUserId) => db.ServiceRequests.AsNoTracking().Where(x =>
        x.Assignments.Any(a => a.StaffUserId == staffUserId &&
            (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS || a.Status == AssignmentStatus.COMPLETED)));

    private IQueryable<ServiceRequest> ActiveRequestAsync(Guid staffUserId, Guid id) => db.ServiceRequests.AsNoTracking().Where(x => x.Id == id &&
        x.Assignments.Any(a => a.StaffUserId == staffUserId &&
            (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS)));

    private static IQueryable<StaffServiceRequestCore> ProjectCore(IQueryable<ServiceRequest> requests, Guid staffUserId) => requests.Select(x =>
        new StaffServiceRequestCore(x.Id, x.RequestNumber, x.Title, x.Description, x.Category == null ? null : x.Category.Name,
            x.ApartmentUnitId, x.FacilityId, x.EquipmentId, x.Status.ToString(), x.FinalPriorityCode,
            x.ServiceAreaCode, x.PreferredDate, x.PreferredTimeCode,
            x.Assignments.Where(a => a.StaffUserId == staffUserId &&
                    (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS || a.Status == AssignmentStatus.COMPLETED))
                .OrderByDescending(a => a.AssignedAt).Select(a => a.Id).First(),
            x.Assignments.Where(a => a.StaffUserId == staffUserId &&
                    (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS || a.Status == AssignmentStatus.COMPLETED))
                .OrderByDescending(a => a.AssignedAt).Select(a => a.AssignedAt).First(),
            x.Assignments.Where(a => a.StaffUserId == staffUserId &&
                    (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS || a.Status == AssignmentStatus.COMPLETED))
                .OrderByDescending(a => a.AssignedAt).Select(a => a.StartedAt).FirstOrDefault(), x.UpdatedAt));

    private async Task<(ServiceRequest? Request, ServiceRequestAssignment? Assignment)> CurrentAsync(Guid staffUserId, Guid id, CancellationToken ct)
    {
        var request = await db.ServiceRequests.Include(x => x.Assignments).Include(x => x.Activities).SingleOrDefaultAsync(x => x.Id == id, ct);
        var assignment = request?.Assignments.Where(x => x.StaffUserId == staffUserId && x.IsActive).OrderByDescending(x => x.AssignedAt).FirstOrDefault();
        return (request, assignment);
    }

    private async Task<StaffServiceRequestOutcome> SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return StaffServiceRequestOutcome.Success; }
        catch (DbUpdateException) { return StaffServiceRequestOutcome.Conflict; }
    }

}
