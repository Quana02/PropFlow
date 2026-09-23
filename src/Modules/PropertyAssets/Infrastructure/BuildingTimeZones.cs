using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;

public sealed class BuildingTimeZones(PropertyAssetsDbContext db) : IBuildingTimeZones
{
    public async Task<IReadOnlyList<BuildingTimeZone>> GetAllAsync(CancellationToken ct) =>
        await db.Buildings.AsNoTracking()
            .Select(building => new BuildingTimeZone(building.Id, building.TimeZoneId))
            .ToArrayAsync(ct);
}
