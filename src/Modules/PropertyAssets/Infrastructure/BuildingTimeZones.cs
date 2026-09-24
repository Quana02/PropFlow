using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;

public sealed class BuildingTimeZones(PropertyAssetsDbContext db) : IBuildingTimeZones
{
    public async Task<string> GetSystemTimeZoneAsync(CancellationToken ct)
    {
        var timeZones = await db.Buildings.AsNoTracking()
            .Select(b => b.TimeZoneId)
            .ToArrayAsync(ct);

        return timeZones.Length switch
        {
            0 => throw new InvalidOperationException("System building profile is not configured."),
            1 => timeZones[0],
            _ => throw new InvalidOperationException("Multiple building profiles found. System invariant violated.")
        };
    }
}
