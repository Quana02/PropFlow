using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Infrastructure;

// One person-level decision shared by registration, OTP revalidation and account linking.
public sealed class ResidentAccountEligibility(ResidentsDbContext db, IApartmentResidentRelationshipSource apartments)
{
    public async Task<Guid?> EligibleApartmentAsync(Guid residentId, DateOnly date, CancellationToken ct)
    {
        if (!await db.Residents.AnyAsync(x => x.Id == residentId && x.Status == ResidentStatus.ACTIVE, ct)) return null;
        var ownership = (await apartments.GetOwnershipsAsync([residentId], ct))
            .Where(x => x.StartDate <= date && x.EndDate == null).OrderBy(x => x.UnitNumber, StringComparer.Ordinal).FirstOrDefault();
        if (ownership is not null) return ownership.ApartmentUnitId;
        var rentedApartments = await db.ResidentApartments.AsNoTracking().Where(x => x.ResidentId == residentId
            && x.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD && x.ResidencyType == ResidencyType.TENANT && x.Status == ResidencyStatus.ACTIVE
            && x.StartDate <= date && (x.EndDate == null || x.EndDate >= date))
            .OrderBy(x => x.ApartmentUnitId).Select(x => x.ApartmentUnitId).ToArrayAsync(ct);
        foreach (var apartmentId in rentedApartments)
            if ((await apartments.GetCurrentOwnerResidentIdsAsync(apartmentId, date, ct)).Count == 0)
                return apartmentId;
        return null;
    }
}
