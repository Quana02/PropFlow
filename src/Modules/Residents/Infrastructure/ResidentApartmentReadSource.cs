using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Infrastructure;

public sealed class ResidentApartmentReadSource(ResidentsDbContext db) : IResidentApartmentReadSource
{
    public Task<bool> HasActiveResidenciesAsync(Guid apartmentUnitId, DateOnly date, CancellationToken cancellationToken = default) =>
        db.ResidentApartments.AsNoTracking().AnyAsync(
            x => x.ApartmentUnitId == apartmentUnitId && x.Status == ResidencyStatus.ACTIVE && x.StartDate <= date && (x.EndDate == null || x.EndDate >= date),
            cancellationToken);

    public Task<bool> HasActiveOwnerOccupiedResidencyAsync(Guid residentId, Guid apartmentUnitId, DateOnly date, CancellationToken cancellationToken = default) =>
        db.ResidentApartments.AsNoTracking().AnyAsync(x => x.ResidentId == residentId && x.ApartmentUnitId == apartmentUnitId &&
            x.ResidencyType == ResidencyType.OWNER_OCCUPIED && x.Status == ResidencyStatus.ACTIVE &&
            x.StartDate <= date && (x.EndDate == null || x.EndDate >= date), cancellationToken);

    public async Task<IReadOnlyList<ResidentLookupItem>> SearchResidentsAsync(string? search, int take, CancellationToken cancellationToken = default)
    {
        var query = db.Residents.AsNoTracking().Where(x => x.Status == Domain.Residents.ResidentStatus.ACTIVE);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpper();
            query = query.Where(x => x.ResidentCode.ToUpper().Contains(term) || x.FullName.ToUpper().Contains(term));
        }
        return await query.OrderBy(x => x.ResidentCode).Take(Math.Clamp(take, 1, 50))
            .Select(x => new ResidentLookupItem(x.Id, x.ResidentCode, x.FullName)).ToListAsync(cancellationToken);
    }

    public Task<bool> ResidentExistsAsync(Guid residentId, CancellationToken cancellationToken = default) =>
        db.Residents.AsNoTracking().AnyAsync(x => x.Id == residentId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ResidentLookupItem>> GetResidentsAsync(IReadOnlyCollection<Guid> residentIds, CancellationToken cancellationToken = default)
    {
        if (residentIds.Count == 0) return new Dictionary<Guid, ResidentLookupItem>();
        return await db.Residents.AsNoTracking().Where(x => residentIds.Contains(x.Id))
            .Select(x => new ResidentLookupItem(x.Id, x.ResidentCode, x.FullName))
            .ToDictionaryAsync(x => x.ResidentId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ActiveResidentAssociation>>> GetActiveAssociationsAsync(IReadOnlyCollection<Guid> apartmentUnitIds, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (apartmentUnitIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<ActiveResidentAssociation>>();
        var rows = await db.ResidentApartments.AsNoTracking()
            .Where(x => apartmentUnitIds.Contains(x.ApartmentUnitId) && x.Status == ResidencyStatus.ACTIVE && x.StartDate <= date && (x.EndDate == null || x.EndDate >= date))
            .Join(db.Residents.AsNoTracking(), x => x.ResidentId, r => r.Id, (x, r) => new { x, r })
            .OrderBy(x => x.r.ResidentCode)
            .Select(x => new { x.x.ApartmentUnitId, Item = new ActiveResidentAssociation(x.r.Id, x.r.ResidentCode, x.r.FullName, x.x.HouseholdRole.ToString(), x.x.ResidencyType.ToString(), x.x.RelationshipToHead == null ? null : x.x.RelationshipToHead.ToString(), x.x.StartDate, x.x.Status.ToString()) })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(x => x.ApartmentUnitId).ToDictionary(x => x.Key, x => (IReadOnlyList<ActiveResidentAssociation>)x.Select(y => y.Item).ToList());
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResidentAssociationItem>>> GetAssociationsAsync(IReadOnlyCollection<Guid> apartmentUnitIds, CancellationToken cancellationToken = default)
    {
        if (apartmentUnitIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<ResidentAssociationItem>>();
        var relations = await db.ResidentApartments.AsNoTracking()
            .Where(x => apartmentUnitIds.Contains(x.ApartmentUnitId))
            .ToListAsync(cancellationToken);
        var residentIds = relations.Select(x => x.ResidentId).Distinct().ToArray();
        var people = await db.Residents.AsNoTracking().Where(x => residentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ResidentCode, x.FullName }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var relationById = relations.ToDictionary(x => x.Id);
        var rows = relations.Select(relation =>
        {
            var resident = people[relation.ResidentId];
            var head = relation.HouseholdHeadResidencyId is Guid headId && relationById.TryGetValue(headId, out var headRelation)
                ? people.GetValueOrDefault(headRelation.ResidentId) : null;
            return new
            {
                relation.ApartmentUnitId,
                Item = new ResidentAssociationItem(relation.Id, resident.Id, resident.ResidentCode, resident.FullName,
                    relation.HouseholdRole.ToString(), relation.ResidencyType.ToString(), relation.HouseholdHeadResidencyId,
                    head?.FullName, head?.ResidentCode, relation.RelationshipToHead?.ToString(), relation.StartDate,
                    relation.EndDate, relation.Status.ToString())
            };
        }).OrderByDescending(x => x.Item.StartDate);
        return rows.GroupBy(x => x.ApartmentUnitId).ToDictionary(x => x.Key,
            x => (IReadOnlyList<ResidentAssociationItem>)x.Select(y => y.Item).ToList());
    }

    public async Task<IReadOnlyList<Guid>> GetActiveApartmentIdsForUserAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default) =>
        await db.ResidentApartments.AsNoTracking()
            .Where(x => x.Resident!.UserId == userId && x.Status == ResidencyStatus.ACTIVE && x.StartDate <= date && (x.EndDate == null || x.EndDate >= date))
            .OrderBy(x => x.ApartmentUnitId).Select(x => x.ApartmentUnitId).Distinct().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await db.ResidentApartments.AsNoTracking()
            .Where(x => x.Status == ResidencyStatus.ACTIVE && x.StartDate <= date && (x.EndDate == null || x.EndDate >= date))
            .Select(x => x.ApartmentUnitId).Distinct().ToListAsync(cancellationToken);
}
