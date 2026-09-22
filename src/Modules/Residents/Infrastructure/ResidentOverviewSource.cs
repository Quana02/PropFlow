using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Infrastructure;

public sealed class ResidentOverviewSource(ResidentsDbContext db) : IResidentOverviewSource
{
    public async Task<ResidentOverviewCounts> GetCountsAsync(IReadOnlyList<ApartmentOccupancyAtDate> apartments, CancellationToken ct)
    {
        var residentCount = await db.Residents.AsNoTracking().CountAsync(resident => resident.Status == ResidentStatus.ACTIVE, ct);
        var occupied = new HashSet<Guid>();
        foreach (var group in apartments)
        {
            if (group.ActiveApartmentIds.Count == 0) continue;
            // IsActiveAt is the domain rule; the equivalent predicates keep the filter in PostgreSQL.
            var ids = await db.ResidentApartments.AsNoTracking()
                .Where(relation => group.ActiveApartmentIds.Contains(relation.ApartmentUnitId)
                    && relation.Status == ResidencyStatus.ACTIVE
                    && relation.StartDate <= group.Date
                    && (relation.EndDate == null || relation.EndDate >= group.Date))
                .Select(relation => relation.ApartmentUnitId).Distinct().ToArrayAsync(ct);
            occupied.UnionWith(ids);
        }
        return new ResidentOverviewCounts(residentCount, occupied.Count);
    }
}
