using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.Modules.Apartments.Infrastructure;

public sealed class ApartmentResidentRelationshipSource(ApartmentsDbContext db)
    : IApartmentResidentRelationshipSource
{
    public async Task<ApartmentDirectoryScope> GetDirectoryScopeAsync(int? floorNumber, Guid? apartmentUnitId, DateOnly date, CancellationToken ct,
        IReadOnlyCollection<int>? floorNumbers = null, IReadOnlyCollection<Guid>? apartmentUnitIds = null)
    {
        var units=db.ApartmentUnits.AsNoTracking().AsQueryable();
        if(floorNumber.HasValue)units=units.Where(x=>x.FloorNumber==floorNumber.Value);
        if(apartmentUnitId.HasValue)units=units.Where(x=>x.Id==apartmentUnitId.Value);
        if(floorNumbers is {Count:>0})units=units.Where(x=>floorNumbers.Contains(x.FloorNumber));
        if(apartmentUnitIds is {Count:>0})units=units.Where(x=>apartmentUnitIds.Contains(x.Id));
        var ids=await units.Select(x=>x.Id).ToArrayAsync(ct);
        var owners=await db.ApartmentOwnerships.AsNoTracking().Where(x=>ids.Contains(x.ApartmentUnitId)&&x.StartDate<=date&&x.EndDate==null).Select(x=>x.OwnerResidentId).Distinct().ToArrayAsync(ct);
        return new(ids,owners);
    }
    public async Task<IReadOnlyList<ApartmentReference>> SearchApartmentsAsync(
        string? search,
        int take,
        CancellationToken ct)
    {
        if (take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        var query = db.ApartmentUnits.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(unit => EF.Functions.ILike(unit.UnitNumber, $"%{term}%"));
        }
        return await query.OrderBy(unit => unit.FloorNumber).ThenBy(unit => unit.UnitNumber)
            .Take(take)
            .Select(unit => new ApartmentReference(unit.Id, unit.UnitNumber, unit.FloorNumber))
            .ToArrayAsync(ct);
    }

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
