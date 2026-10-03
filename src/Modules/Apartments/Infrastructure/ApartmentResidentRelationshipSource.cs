using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.Modules.Apartments.Infrastructure;

public sealed class ApartmentResidentRelationshipSource(ApartmentsDbContext db)
    : IApartmentResidentRelationshipSource
{
    public async Task<IReadOnlyList<ApartmentReference>> GetApartmentsAsync(
        IReadOnlyCollection<Guid> apartmentUnitIds,
        CancellationToken ct)
    {
        if (apartmentUnitIds.Count == 0) return [];

        return await db.ApartmentUnits.AsNoTracking()
            .Where(unit => apartmentUnitIds.Contains(unit.Id))
            .OrderBy(unit => unit.UnitNumber)
            .Select(unit => new ApartmentReference(unit.Id, unit.UnitNumber, unit.FloorNumber))
            .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<ResidentApartmentOwnership>> GetOwnershipsAsync(
        IReadOnlyCollection<Guid> residentIds,
        CancellationToken ct)
    {
        if (residentIds.Count == 0) return [];

        return await (
            from ownership in db.ApartmentOwnerships.AsNoTracking()
            join unit in db.ApartmentUnits.AsNoTracking() on ownership.ApartmentUnitId equals unit.Id
            where residentIds.Contains(ownership.OwnerResidentId)
            orderby ownership.StartDate descending, unit.UnitNumber, ownership.Id
            select new ResidentApartmentOwnership(
                ownership.Id,
                ownership.OwnerResidentId,
                ownership.ApartmentUnitId,
                unit.UnitNumber,
                unit.FloorNumber,
                ownership.StartDate,
                ownership.EndDate))
            .ToArrayAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetCurrentOwnerResidentIdsAsync(
        Guid apartmentUnitId,
        DateOnly date,
        CancellationToken ct) =>
        await db.ApartmentOwnerships.AsNoTracking()
            .Where(x => x.ApartmentUnitId == apartmentUnitId && x.StartDate <= date && x.EndDate == null)
            .Select(x => x.OwnerResidentId)
            .Distinct()
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetCurrentOwnerResidentIdsAsync(
        DateOnly date,
        CancellationToken ct) =>
        await db.ApartmentOwnerships.AsNoTracking()
            .Where(x => x.StartDate <= date && x.EndDate == null)
            .Select(x => x.OwnerResidentId)
            .Distinct()
            .ToArrayAsync(ct);
}
