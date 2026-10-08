using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.Maintenance.Infrastructure;

public sealed class MaintenanceAssignedAssetSource(MaintenanceDbContext db) : IAssignedAssetSource
{
    public async Task<IReadOnlyList<AssignedAssetReference>> GetActiveAssignmentsAsync(Guid staffUserId, CancellationToken ct) =>
        await db.MaintenanceAssignments.AsNoTracking()
            // The domain's IsActive definition, expressed as SQL-translatable predicates.
            .Where(a => a.StaffUserId == staffUserId &&
                (a.Status == AssignmentStatus.ASSIGNED || a.Status == AssignmentStatus.IN_PROGRESS))
            .Select(a => new AssignedAssetReference(a.MaintenanceTask!.FacilityId, a.MaintenanceTask.EquipmentId))
            .ToListAsync(ct);
}
