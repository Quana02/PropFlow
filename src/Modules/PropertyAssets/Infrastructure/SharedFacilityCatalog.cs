using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;

public sealed class SharedFacilityCatalog(PropertyAssetsDbContext db) : ISharedFacilityCatalog
{
    public async Task<IReadOnlyList<SharedFacilityReference>> ListAsync(CancellationToken ct) =>
        await db.Facilities.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new SharedFacilityReference(x.Id, x.Name,
                x.FacilityType == null ? null : x.FacilityType.Name, x.LocationDescription,
                x.Status.ToString(), x.UpdatedAt)).ToArrayAsync(ct);

    public Task<SharedFacilityReference?> FindAsync(Guid facilityId, CancellationToken ct) =>
        db.Facilities.AsNoTracking().Where(x => x.Id == facilityId)
            .Select(x => new SharedFacilityReference(x.Id, x.Name,
                x.FacilityType == null ? null : x.FacilityType.Name, x.LocationDescription,
                x.Status.ToString(), x.UpdatedAt)).SingleOrDefaultAsync(ct);
}
