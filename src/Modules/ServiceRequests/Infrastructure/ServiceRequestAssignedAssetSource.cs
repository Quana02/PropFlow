using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class ServiceRequestAssignedAssetSource(ServiceRequestsDbContext db) : IAssignedAssetSource
{
    public async Task<IReadOnlyList<AssignedAssetReference>> GetActiveAssignmentsAsync(Guid staffUserId, CancellationToken ct) =>
        await db.ServiceRequestAssignments.AsNoTracking()
            // Keep aligned with ServiceRequestAssignment.IsActive.
            .Where(a => a.StaffUserId == staffUserId &&
                (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS))
            .Select(a => new AssignedAssetReference(a.ServiceRequest.FacilityId, a.ServiceRequest.EquipmentId))
            .ToListAsync(ct);
}
