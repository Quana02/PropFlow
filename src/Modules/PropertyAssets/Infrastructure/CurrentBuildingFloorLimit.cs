using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;
public sealed class CurrentBuildingFloorLimit(PropertyAssetsDbContext db) : ICurrentBuildingFloorLimit
{
    public Task<int?> GetMaximumFloorAsync(CancellationToken ct) => db.Buildings.AsNoTracking()
        .OrderByDescending(x => x.Status == MasterDataStatus.ACTIVE)
        .ThenByDescending(x => x.UpdatedAt).ThenByDescending(x => x.CreatedAt)
        .Select(x => (int?)x.NumberOfFloors).FirstOrDefaultAsync(ct);
}
