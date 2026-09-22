using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.Modules.Apartments.Infrastructure;

public sealed class ApartmentOverviewSource(ApartmentsDbContext db) : IApartmentOverviewSource
{
    public async Task<IReadOnlyList<ActiveApartmentIds>> GetActiveApartmentsAsync(CancellationToken ct)
    {
        var units = await db.ApartmentUnits.AsNoTracking()
            .Where(unit => unit.Status == MasterDataStatus.ACTIVE)
            .Select(unit => new { unit.BuildingId, unit.Id }).ToArrayAsync(ct);
        return units.GroupBy(unit => unit.BuildingId)
            .Select(group => new ActiveApartmentIds(group.Key, group.Select(unit => unit.Id).ToArray())).ToArray();
    }
}
