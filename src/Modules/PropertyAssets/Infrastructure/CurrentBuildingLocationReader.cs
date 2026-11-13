using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;

public sealed class CurrentBuildingLocationReader(PropertyAssetsDbContext db) : ICurrentBuildingLocation
{
    public Task<CurrentBuildingLocation?> GetAsync(CancellationToken ct) => db.Buildings.AsNoTracking()
        .OrderByDescending(b => b.Status == MasterDataStatus.ACTIVE)
        .ThenByDescending(b => b.UpdatedAt).ThenByDescending(b => b.CreatedAt)
        .Select(b => new CurrentBuildingLocation(b.Address, b.TimeZoneId)).FirstOrDefaultAsync(ct);
}
