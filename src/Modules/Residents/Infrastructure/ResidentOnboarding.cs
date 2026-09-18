using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Infrastructure;

public sealed class ResidentOnboarding(ResidentsDbContext db) : IResidentOnboarding
{
    public async Task<EligibleResident?> FindEligibleAsync(string normalizedEmail, DateOnly today, CancellationToken ct)
    {
        var matches = await db.Residents.AsNoTracking()
            .Where(r => r.Email != null && r.Email.Trim().ToUpper() == normalizedEmail)
            .Select(r => new { r.Id, r.UserId, r.Status }).Take(2).ToListAsync(ct);
        if (matches.Count != 1 || matches[0].UserId != null || matches[0].Status != ResidentStatus.ACTIVE) return null;
        var id = matches[0].Id;
        var apartment = await db.ResidentApartments.Where(r => r.ResidentId == id && r.Status == ResidencyStatus.ACTIVE
            && r.StartDate <= today && (r.EndDate == null || r.EndDate >= today))
            .OrderByDescending(r => r.IsPrimary).ThenBy(r => r.Id).Select(r => (Guid?)r.ApartmentUnitId).FirstOrDefaultAsync(ct);
        return apartment == null ? null : new EligibleResident(id, apartment);
    }

    public async Task<bool> LinkEligibleAsync(Guid residentId, Guid userId, string normalizedEmail, DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        if ((await FindEligibleAsync(normalizedEmail, today, ct))?.Id != residentId) return false;
        // Conditional update prevents another account claiming the same resident concurrently.
        return await db.Residents.Where(r => r.Id == residentId && r.UserId == null && r.Status == ResidentStatus.ACTIVE
                && r.Email != null && r.Email.Trim().ToUpper() == normalizedEmail
                && db.ResidentApartments.Any(a => a.ResidentId == r.Id && a.Status == ResidencyStatus.ACTIVE
                    && a.StartDate <= today && (a.EndDate == null || a.EndDate >= today)))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UserId, userId).SetProperty(r => r.UpdatedAt, now).SetProperty(r => r.UpdatedBy, userId), ct) == 1;
    }
}
