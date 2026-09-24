using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Application;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence;

public sealed class EfApartmentStatisticsReader(ApartmentsDbContext db) : IApartmentStatisticsReader
{
    public Task<int> GetTotalApartmentsAsync(CancellationToken cancellationToken = default)
    {
        return db.ApartmentUnits.CountAsync(cancellationToken);
    }
}
