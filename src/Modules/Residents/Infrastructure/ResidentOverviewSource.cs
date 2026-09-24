using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Infrastructure;

public sealed class ResidentOverviewSource(ResidentsDbContext db) : IResidentOverviewSource
{
    public async Task<ResidentOverviewCounts> GetCountsAsync(
        DateOnly date,
        IReadOnlyCollection<Guid> activeApartmentIds,
        CancellationToken ct)
    {
        var residentCount = await db.Residents.AsNoTracking().CountAsync(resident => resident.Status == ResidentStatus.ACTIVE, ct);
        if (activeApartmentIds.Count == 0)
            return new ResidentOverviewCounts(residentCount, 0);

        // IsActiveAt is the domain rule; the equivalent predicates keep the filter in PostgreSQL.
        var occupiedCount = await db.ResidentApartments.AsNoTracking()
            .Where(relation => activeApartmentIds.Contains(relation.ApartmentUnitId)
                && relation.Status == ResidencyStatus.ACTIVE
                && relation.StartDate <= date
                && (relation.EndDate == null || relation.EndDate >= date))
            .Select(relation => relation.ApartmentUnitId)
            .Distinct()
            .CountAsync(ct);

        return new ResidentOverviewCounts(residentCount, occupiedCount);
    }
}
