using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;

namespace PropFlow.Api.Composition;

// Joins module-owned data only at the composition root. Ownership and residency remain distinct.
public sealed class BillingHouseholdDirectory(ResidentsDbContext residents,
    ApartmentsDbContext apartments, AuthenticationDbContext accounts) : IBillingHouseholdDirectory
{
    public async Task<IReadOnlyDictionary<Guid, int>> ResidentCountsAsync(IReadOnlyCollection<Guid> apartmentIds, DateOnly date, CancellationToken ct)
    {
        // Count people, not accounts or household heads. Ended rows preserve who lived here on the closing date.
        var rows = await residents.ResidentApartments.AsNoTracking()
            .Where(x => apartmentIds.Contains(x.ApartmentUnitId) && x.StartDate <= date
                && (x.EndDate == null || x.EndDate >= date))
            .Select(x => new { x.ApartmentUnitId, x.ResidentId }).Distinct().ToArrayAsync(ct);
        return rows.GroupBy(x => x.ApartmentUnitId).ToDictionary(x => x.Key, x => x.Count());
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> LinkedAccountsAsync(IReadOnlyCollection<Guid> residentIds, CancellationToken ct)
    {
        var linked = await residents.Residents.AsNoTracking().Where(x => residentIds.Contains(x.Id) && x.UserId != null)
            .Select(x => new { x.Id, UserId = x.UserId!.Value }).ToArrayAsync(ct);
        var ids = linked.Select(x => x.UserId).ToArray();
        var active = await accounts.UserAccounts.AsNoTracking().Where(x => ids.Contains(x.Id) && x.Status == AccountStatus.ACTIVE)
            .Select(x => x.Id).ToArrayAsync(ct);
        return linked.Where(x => active.Contains(x.UserId)).ToDictionary(x => x.Id, x => x.UserId);
    }
    public Task<string?> RecipientEmailAsync(Guid? userId, string? snapshotEmail, CancellationToken ct) => userId is null
        ? Task.FromResult(snapshotEmail)
        : accounts.UserAccounts.AsNoTracking().Where(x => x.Id == userId && x.Status == AccountStatus.ACTIVE).Select(x => x.Email).SingleOrDefaultAsync(ct);
    public async Task<IReadOnlyList<BillingApartment>> ManagedApartmentsAsync(CancellationToken ct) =>
        await apartments.ApartmentUnits.AsNoTracking().Where(x => x.Status == PropFlow.Modules.Apartments.Domain.ApartmentUnits.MasterDataStatus.ACTIVE)
            .OrderBy(x => x.UnitNumber).Select(x => new BillingApartment(x.Id, x.UnitNumber, x.ApartmentUnitTypeId, x.ApartmentUnitType.Name)).ToArrayAsync(ct);

    public Task<IReadOnlyList<BillingHousehold>> ListChargeableAsync(DateOnly date, CancellationToken ct) => ResolveAsync(date, false, ct);
    private async Task<IReadOnlyList<BillingHousehold>> ResolveAsync(DateOnly date, bool historical, CancellationToken ct)
    {
        var units = await apartments.ApartmentUnits.AsNoTracking().Where(x => x.Status == PropFlow.Modules.Apartments.Domain.ApartmentUnits.MasterDataStatus.ACTIVE).ToDictionaryAsync(x => x.Id, ct);
        var owners = await apartments.ApartmentOwnerships.AsNoTracking().Where(x => x.StartDate <= date
            && (historical ? x.EndDate == null || x.EndDate >= date : x.EndDate == null))
            .Select(x => new { x.ApartmentUnitId, ResidentId = x.OwnerResidentId }).ToArrayAsync(ct);
        var periodEnd = new DateOnly(date.Year, date.Month, 1).AddMonths(1).AddDays(-1);
        var periodStart = new DateOnly(date.Year, date.Month, 1);
        var heads = await residents.ResidentApartments.AsNoTracking().Where(x => x.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD
            && (x.Status == ResidencyStatus.ACTIVE || x.Status == ResidencyStatus.ENDED)
            && x.StartDate <= (historical ? date : periodEnd) && (x.EndDate == null || x.EndDate >= (historical ? date : periodStart)))
            .Select(x => new { x.ApartmentUnitId, x.ResidentId, x.ResidencyType, x.StartDate, x.EndDate }).ToArrayAsync(ct);
        var ownedIds = owners.Select(x => x.ApartmentUnitId).ToHashSet();
        var parties = owners.Select(x => (x.ApartmentUnitId, x.ResidentId, Capacity: "OWNER", Start: (DateOnly?)null, End: (DateOnly?)null))
            .Concat(heads.Where(x => !ownedIds.Contains(x.ApartmentUnitId)).Select(x => (x.ApartmentUnitId, x.ResidentId,
                Capacity: x.ResidencyType == ResidencyType.TENANT ? "RENTAL" : "HOUSEHOLD_HEAD", Start: (DateOnly?)x.StartDate, End: x.EndDate))).Distinct().ToArray();
        var ids = parties.Select(x => x.ResidentId).Distinct().ToArray();
        var people = await residents.Residents.AsNoTracking().Where(x => ids.Contains(x.Id) && x.Status == ResidentStatus.ACTIVE).ToDictionaryAsync(x => x.Id, ct);
        var userIds = people.Values.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).ToArray();
        var users = await accounts.UserAccounts.AsNoTracking().Where(x => userIds.Contains(x.Id) && x.Status == AccountStatus.ACTIVE).ToDictionaryAsync(x => x.Id, ct);
        return parties.Where(x => units.ContainsKey(x.ApartmentUnitId) && people.ContainsKey(x.ResidentId)).Select(x => {
            var person = people[x.ResidentId];
            var user = person.UserId is Guid id && users.TryGetValue(id, out var account) ? account : null;
            var unit = units[x.ApartmentUnitId];
            return new BillingHousehold(unit.Id, unit.UnitNumber, unit.UsableAreaM2, user?.Id ?? Guid.Empty,
                person.FullName, user?.Email ?? person.Email, person.Id, x.Capacity, x.Start, x.End, unit.ApartmentUnitTypeId);
        }).ToArray();
    }
    public async Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date, CancellationToken ct) =>
        (await ResolveAsync(date, true, ct)).Where(x => x.UserId != Guid.Empty || !string.IsNullOrWhiteSpace(x.Email)).ToArray();
    public async Task<BillingHousehold?> DescribeAsync(Guid apartmentId, DateOnly date, CancellationToken ct)
    {
        var parties = (await ResolveAsync(date, true, ct)).Where(x => x.ApartmentId == apartmentId).ToArray();
        if (parties.Length == 1) return parties[0];
        var unit = await apartments.ApartmentUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == apartmentId, ct);
        return unit is null ? null : new(unit.Id, unit.UnitNumber, unit.UsableAreaM2, Guid.Empty,
            parties.Length > 1 ? string.Join(", ", parties.Select(x => x.FullName)) : "Chưa xác định bên chịu phí", null);
    }
}
