using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application.Announcements;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Api.Composition;

public sealed class ResidentAnnouncementRecipients(AuthenticationDbContext accounts, AdministrationDbContext administration,
    IResidentResidenceSource residences, ICurrentBuildingTimeZone zone, TimeProvider clock) : IAnnouncementRecipients
{
    public async Task<IReadOnlyList<Guid>> GetUserIdsAsync(CancellationToken ct)
    {
        var residentAccounts = await administration.UserRoleAssignments.AsNoTracking()
            .Where(x => x.Role.Code == "RESIDENT" && x.Role.IsActive)
            .Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        var candidates = await accounts.UserAccounts.AsNoTracking()
            .Where(x => residentAccounts.Contains(x.Id) && x.Status == AccountStatus.ACTIVE)
            .Select(x => x.Id).ToArrayAsync(ct);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById(await zone.GetAsync(ct))).DateTime);
        var recipients = new List<Guid>();
        foreach (var userId in candidates)
            if (await residences.FindActiveAsync(userId, date, ct) is not null)
                recipients.Add(userId);
        return recipients;
    }
}
