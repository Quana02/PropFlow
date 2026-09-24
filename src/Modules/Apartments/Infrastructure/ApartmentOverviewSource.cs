using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.Modules.Apartments.Infrastructure;

public sealed class ApartmentOverviewSource(ApartmentsDbContext db) : IApartmentOverviewSource
{
    public async Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(CancellationToken ct)
    {
        return await db.ApartmentUnits.AsNoTracking()
            .Where(unit => unit.Status == MasterDataStatus.ACTIVE)
            .Select(unit => unit.Id)
            .ToArrayAsync(ct);
    }
}
