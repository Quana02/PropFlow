using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class ManagerServiceRequestStore(ServiceRequestsDbContext db) : IManagerServiceRequestStore
{
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
                x.ToStatus == null ? null : x.ToStatus.ToString(), x.Title, x.Detail, x.PerformedBy, x.CreatedAt))
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
        await db.SaveChangesAsync(ct);
        return ManagerServiceRequestOutcome.Success;
    }
}
