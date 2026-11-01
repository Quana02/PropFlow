using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Application;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence;

public sealed class EfApartmentStatisticsReader(ApartmentsDbContext db) : IApartmentStatisticsReader
{
    public async Task<int> GetHighestApartmentFloorAsync(CancellationToken cancellationToken = default) =>
        await db.ApartmentUnits.MaxAsync(x => (int?)x.FloorNumber, cancellationToken) ?? 0;
    public Task<int> GetTotalApartmentsAsync(CancellationToken cancellationToken = default)
    {
        return db.ApartmentUnits.CountAsync(cancellationToken);
    }
}
