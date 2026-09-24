using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;

public sealed class CurrentBuildingTimeZone(PropertyAssetsDbContext db) : ICurrentBuildingTimeZone
{
    private const string DefaultTimeZoneId = "Asia/Ho_Chi_Minh";

    public async Task<string> GetAsync(CancellationToken ct)
    {
        var timeZone = await db.Buildings.AsNoTracking()
            .OrderByDescending(b => b.Status == MasterDataStatus.ACTIVE)
            .ThenByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.CreatedAt)
            .Select(b => b.TimeZoneId)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZoneId : timeZone;
    }
}
