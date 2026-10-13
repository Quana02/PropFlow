using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class ManagerServiceRequestStore(ServiceRequestsDbContext db, TimeProvider? clock = null) : IManagerServiceRequestStore
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public async Task<ManagerServiceRequestPage> ListAsync(ManagerServiceRequestListQuery query, CancellationToken ct)
    {
        var requests = db.ServiceRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            requests = requests.Where(x => EF.Functions.ILike(x.RequestNumber, $"%{term}%") ||
                                           EF.Functions.ILike(x.Title, $"%{term}%"));
        }
        if (query.Status == "ACTIVE")
            requests = requests.Where(x => x.Status == ServiceRequestStatus.UNDER_REVIEW || x.Status == ServiceRequestStatus.ASSIGNED || x.Status == ServiceRequestStatus.IN_PROGRESS);
        else if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<ServiceRequestStatus>(query.Status, out var status))
            requests = requests.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Priority))
            requests = requests.Where(x => x.FinalPriorityCode == query.Priority);
        if (query.UnassignedOnly)
            requests = requests.Where(x => !x.Assignments.Any(a => a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS));
        if (query.SubmittedWithinDays is { } days)
        {
            var submittedFrom = timeProvider.GetUtcNow().AddDays(-days);
            requests = requests.Where(x => x.SubmittedAt >= submittedFrom);
        }

        var totalCount = await requests.CountAsync(ct);
        var items = await requests
            .OrderByDescending(x => x.SubmittedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new ManagerServiceRequestCore(
                x.Id, x.RequestNumber, x.Title, x.Description,
                x.Category == null ? null : x.Category.Code,
                x.Category == null ? null : x.Category.Name,
                x.ResidentId, x.ResidentApartmentId, x.ApartmentUnitId, x.FacilityId, x.EquipmentId,
                x.Status.ToString(), x.FinalPriorityCode, x.ServiceAreaCode, x.PreferredDate,
                x.PreferredTimeCode, x.SubmittedAt, x.UpdatedAt,
                x.Assignments.Where(a => a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS)
                    .OrderByDescending(a => a.AssignedAt).Select(a => (Guid?)a.StaffUserId).FirstOrDefault()))
            .ToArrayAsync(ct);

        var summary = await db.ServiceRequests.AsNoTracking().GroupBy(_ => 1).Select(group =>
            new ManagerServiceRequestSummary(
                group.Count(),
                group.Count(x => x.Status == ServiceRequestStatus.SUBMITTED),
                group.Count(x => x.Status == ServiceRequestStatus.UNDER_REVIEW || x.Status == ServiceRequestStatus.ASSIGNED || x.Status == ServiceRequestStatus.IN_PROGRESS),
                group.Count(x => x.Status == ServiceRequestStatus.RESOLVED || x.Status == ServiceRequestStatus.CLOSED)))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0);

        return new(items, totalCount, summary);
    }

    public Task<ManagerServiceRequestCore?> GetAsync(Guid id, CancellationToken ct) => db.ServiceRequests
        .AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new ManagerServiceRequestCore(
            x.Id, x.RequestNumber, x.Title, x.Description,
            x.Category == null ? null : x.Category.Code,
            x.Category == null ? null : x.Category.Name,
            x.ResidentId, x.ResidentApartmentId, x.ApartmentUnitId, x.FacilityId, x.EquipmentId,
            x.Status.ToString(), x.FinalPriorityCode, x.ServiceAreaCode, x.PreferredDate,
            x.PreferredTimeCode, x.SubmittedAt, x.UpdatedAt,
            x.Assignments.Where(a => a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS)
                .OrderByDescending(a => a.AssignedAt).Select(a => (Guid?)a.StaffUserId).FirstOrDefault()))
        .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ManagerServiceRequestActivityItem>> GetActivitiesAsync(Guid id, CancellationToken ct) =>
        await db.ServiceRequestActivities.AsNoTracking()
            .Where(x => x.ServiceRequestId == id)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new ManagerServiceRequestActivityItem(
                x.Id, x.ActivityType.ToString(), x.FromStatus == null ? null : x.FromStatus.ToString(),
                x.ToStatus == null ? null : x.ToStatus.ToString(), x.Title, x.Detail, x.WorkResult, x.PerformedBy, x.CreatedAt))
            .ToArrayAsync(ct);

    public async Task<ManagerServiceRequestOutcome> ReviewAsync(
        Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status != ServiceRequestStatus.SUBMITTED) return ManagerServiceRequestOutcome.Conflict;

        request.MarkUnderReview(now);
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id, ServiceActivityType.MANAGER_REVIEWED, now,
            fromStatus: ServiceRequestStatus.SUBMITTED,
            toStatus: ServiceRequestStatus.UNDER_REVIEW,
            title: "Ban Quản lý đã tiếp nhận yêu cầu",
            detail: note,
            performedBy: managerUserId));
        try
        {
            await db.SaveChangesAsync(ct);
            return ManagerServiceRequestOutcome.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagerServiceRequestOutcome.Conflict;
        }
    }

    public async Task<ManagerServiceRequestOutcome> ConfirmPropertyContextAsync(
        Guid id, Guid managerUserId, Guid? apartmentUnitId, Guid? facilityId, Guid? equipmentId,
        string? serviceAreaCode, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status != ServiceRequestStatus.SUBMITTED) return ManagerServiceRequestOutcome.Conflict;

        request.AssociatePropertyContext(apartmentUnitId, facilityId, equipmentId, now);
        if (!string.Equals(request.ServiceAreaCode, serviceAreaCode, StringComparison.OrdinalIgnoreCase))
            request.UpdateServiceArea(serviceAreaCode, now);
        request.MarkUnderReview(now);

        var contextDetail = $"ApartmentUnitId={apartmentUnitId?.ToString() ?? "null"}; FacilityId={facilityId?.ToString() ?? "null"}; EquipmentId={equipmentId?.ToString() ?? "null"}; ServiceAreaCode={serviceAreaCode ?? "null"}.";
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id, ServiceActivityType.PROPERTY_CONTEXT_ASSOCIATED, now,
            title: "Đã gắn bối cảnh tài sản cho đơn dịch vụ",
            detail: contextDetail,
            performedBy: managerUserId));
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id, ServiceActivityType.MANAGER_REVIEWED, now,
            fromStatus: ServiceRequestStatus.SUBMITTED,
            toStatus: ServiceRequestStatus.UNDER_REVIEW,
            title: "Ban Quản lý đã xác nhận đơn dịch vụ",
            detail: note,
            performedBy: managerUserId));
        try
        {
            await db.SaveChangesAsync(ct);
            return ManagerServiceRequestOutcome.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagerServiceRequestOutcome.Conflict;
        }
    }

    public async Task<ManagerServiceRequestOutcome> AssignAsync(
        Guid id, Guid managerUserId, Guid staffUserId, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests
            .Include(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status is not (ServiceRequestStatus.UNDER_REVIEW or ServiceRequestStatus.ASSIGNED or ServiceRequestStatus.IN_PROGRESS))
            return ManagerServiceRequestOutcome.Conflict;

        var activeAssignment = request.Assignments
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.AssignedAt)
            .FirstOrDefault();
        if (activeAssignment?.StaffUserId == staffUserId)
            return ManagerServiceRequestOutcome.Conflict;

        var previousStatus = request.Status;
        var isReassignment = activeAssignment is not null;
        if (activeAssignment is not null)
            activeAssignment.EndAsReassigned(now);

        var assignment = new ServiceRequestAssignment(request.Id, staffUserId, managerUserId, now, note);
        db.ServiceRequestAssignments.Add(assignment);
        request.MarkAssigned(now);
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id,
            isReassignment ? ServiceActivityType.REASSIGNED : ServiceActivityType.ASSIGNED,
            now,
            assignmentId: assignment.Id,
            fromStatus: previousStatus,
            toStatus: ServiceRequestStatus.ASSIGNED,
            title: isReassignment ? "Ban Quản lý đã đổi nhân sự phụ trách" : "Ban Quản lý đã phân công nhân sự",
            detail: note,
            performedBy: managerUserId));

        try
        {
            await db.SaveChangesAsync(ct);
            return ManagerServiceRequestOutcome.Success;
        }
        catch (DbUpdateException)
        {
            return ManagerServiceRequestOutcome.Conflict;
        }
    }

    public Task<ManagerServiceRequestOutcome> RequestReworkAsync(
        Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct) =>
        ReviewResolutionAsync(id, managerUserId, note, approve: false, now, ct);

    public Task<ManagerServiceRequestOutcome> ApproveResolutionAsync(
        Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct) =>
        ReviewResolutionAsync(id, managerUserId, note, approve: true, now, ct);

    public async Task<ManagerServiceRequestOutcome> CloseAsync(
        Guid id, Guid managerUserId, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status != ServiceRequestStatus.RESOLVED) return ManagerServiceRequestOutcome.Conflict;

        request.Close(managerUserId, now);
        db.ServiceRequestActivities.Add(new ServiceRequestActivity(
            request.Id,
            ServiceActivityType.CLOSED,
            now,
            fromStatus: ServiceRequestStatus.RESOLVED,
            toStatus: ServiceRequestStatus.CLOSED,
            title: "Ban Quản lý đã đóng yêu cầu",
            detail: note,
            performedBy: managerUserId));

        return await SaveAsync(ct);
    }

    private async Task<ManagerServiceRequestOutcome> ReviewResolutionAsync(
        Guid id, Guid managerUserId, string? note, bool approve, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.ServiceRequests
            .Include(x => x.Assignments)
            .Include(x => x.Activities)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (request is null) return ManagerServiceRequestOutcome.NotFound;
        if (request.Status != ServiceRequestStatus.IN_PROGRESS) return ManagerServiceRequestOutcome.Conflict;

        var latestWorkResult = request.Activities
            .Where(x => x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var latestRework = request.Activities
            .Where(x => x.ActivityType == ServiceActivityType.REWORK_REQUESTED)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (latestWorkResult is null ||
            (latestRework is not null && latestRework.CreatedAt >= latestWorkResult.CreatedAt))
            return ManagerServiceRequestOutcome.Conflict;

        var activeAssignment = request.Assignments
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.AssignedAt)
            .FirstOrDefault();

        if (approve)
        {
            activeAssignment?.Complete(now);
            request.Resolve(now);
            db.ServiceRequestActivities.Add(new ServiceRequestActivity(
                request.Id,
                ServiceActivityType.RESOLVED,
                now,
                assignmentId: activeAssignment?.Id,
                fromStatus: ServiceRequestStatus.IN_PROGRESS,
                toStatus: ServiceRequestStatus.RESOLVED,
                title: "Ban Quản lý đã xác nhận kết quả xử lý",
                detail: note,
                performedBy: managerUserId));
        }
        else
        {
            request.MarkInProgress(now);
            db.ServiceRequestActivities.Add(new ServiceRequestActivity(
                request.Id,
                ServiceActivityType.REWORK_REQUESTED,
                now,
                assignmentId: activeAssignment?.Id,
                fromStatus: ServiceRequestStatus.IN_PROGRESS,
                toStatus: ServiceRequestStatus.IN_PROGRESS,
                title: "Ban Quản lý yêu cầu Staff xử lý lại",
                detail: note,
                performedBy: managerUserId));
        }

        return await SaveAsync(ct);
    }

    private async Task<ManagerServiceRequestOutcome> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return ManagerServiceRequestOutcome.Success;
        }
        catch (DbUpdateException)
        {
            return ManagerServiceRequestOutcome.Conflict;
        }
    }
}
