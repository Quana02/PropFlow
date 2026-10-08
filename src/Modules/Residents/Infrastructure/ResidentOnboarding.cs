using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Infrastructure;

public sealed class ResidentOnboarding(
    ResidentsDbContext db,
    IApartmentResidentRelationshipSource apartmentRelationships) : IResidentOnboarding
{
    public async Task<RegistrationResidentCandidate?> FindRegistrationCandidateAsync(
        string normalizedEmail,
        string normalizedPhone,
        string identityType,
        string normalizedIdentityNumber,
        DateOnly today,
        CancellationToken ct)
    {
        var matches = await db.Residents.AsNoTracking()
            .Where(r => r.Email != null && r.Email.Trim().ToLower() == normalizedEmail
                && r.PhoneNumber != null && r.PhoneNumber.Trim() == normalizedPhone
                && r.IdentityType != null && r.IdentityType.Trim().ToUpper() == identityType
                && r.IdentityNumber != null && r.IdentityNumber == normalizedIdentityNumber)
            .Select(r => new { r.Id, r.UserId, r.Status, r.Email, r.PhoneNumber, r.IdentityType, r.IdentityNumber })
            .OrderBy(r => r.Id)
            .Take(2).ToListAsync(ct);
        if (matches.Count != 1 || matches[0].UserId != null || matches[0].Status != ResidentStatus.ACTIVE) return null;
        var match = matches[0];
        return await EligibleAsync(match.Id, match.Email!, match.PhoneNumber!, match.IdentityType!, match.IdentityNumber!, today, ct);
    }

    public async Task<RegistrationResidentCandidate?> RevalidateRegistrationCandidateAsync(Guid residentId, DateOnly today, CancellationToken ct)
    {
        var match = await db.Residents.AsNoTracking()
            .Where(r => r.Id == residentId && r.UserId == null && r.Status == ResidentStatus.ACTIVE
                && r.Email != null && r.PhoneNumber != null && r.IdentityType != null && r.IdentityNumber != null)
            .Select(r => new { r.Id, r.Email, r.PhoneNumber, r.IdentityType, r.IdentityNumber })
            .SingleOrDefaultAsync(ct);
        return match is null
            ? null
            : await EligibleAsync(match.Id, match.Email!, match.PhoneNumber!, match.IdentityType!, match.IdentityNumber!, today, ct);
    }

    private async Task<RegistrationResidentCandidate?> EligibleAsync(
        Guid id,
        string email,
        string phone,
        string identityType,
        string identityNumber,
        DateOnly today,
        CancellationToken ct)
    {
        var apartment = await db.ResidentApartments.Where(r => r.ResidentId == id && r.Status == ResidencyStatus.ACTIVE
            && r.StartDate <= today && (r.EndDate == null || r.EndDate >= today))
            .OrderByDescending(r => r.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD).ThenBy(r => r.Id).Select(r => (Guid?)r.ApartmentUnitId).FirstOrDefaultAsync(ct);
        if (apartment is null)
        {
            apartment = (await apartmentRelationships.GetOwnershipsAsync([id], ct))
                .Where(ownership => ownership.StartDate <= today && ownership.EndDate is null)
                .OrderByDescending(ownership => ownership.StartDate)
                .ThenBy(ownership => ownership.UnitNumber, StringComparer.Ordinal)
                .ThenBy(ownership => ownership.Id)
                .Select(ownership => (Guid?)ownership.ApartmentUnitId)
                .FirstOrDefault();
        }

        return apartment is null ? null : new RegistrationResidentCandidate(
            id,
            apartment,
            ResidentRegistrationNormalization.Email(email),
            ResidentRegistrationNormalization.Phone(phone),
            ResidentRegistrationNormalization.IdentityType(identityType),
            ResidentRegistrationNormalization.IdentityNumber(identityNumber));
    }

    public async Task<bool> LinkEligibleAsync(RegistrationResidentCandidate candidate, Guid userId, DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var current = await FindRegistrationCandidateAsync(candidate.NormalizedEmail, candidate.NormalizedPhone,
            candidate.IdentityType, candidate.NormalizedIdentityNumber, today, ct);
        if (current?.Id != candidate.Id) return false;
        // Eligibility was re-read immediately above. The conditional update keeps the
        // one Resident -> at most one UserId invariant under concurrent account claims.
        return await db.Residents.Where(r => r.Id == candidate.Id && r.UserId == null && r.Status == ResidentStatus.ACTIVE
                && r.Email != null && r.Email.Trim().ToLower() == candidate.NormalizedEmail
                && r.PhoneNumber != null && r.PhoneNumber.Trim() == candidate.NormalizedPhone
                && r.IdentityType != null && r.IdentityType.Trim().ToUpper() == candidate.IdentityType
                && r.IdentityNumber != null && r.IdentityNumber == candidate.NormalizedIdentityNumber)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UserId, userId).SetProperty(r => r.UpdatedAt, now).SetProperty(r => r.UpdatedBy, userId), ct) == 1;
    }
}
