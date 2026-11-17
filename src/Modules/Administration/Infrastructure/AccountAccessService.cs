using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Domain.UserAccessHistories;
using PropFlow.Modules.Administration.Infrastructure.Persistence;

namespace PropFlow.Modules.Administration.Infrastructure;

public sealed class AccountAccessService(AdministrationDbContext db) : IAccountAccess
{
    public async Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct) =>
        (await GetManyAsync([userId], ct)).GetValueOrDefault(userId);

    public async Task<IReadOnlyDictionary<Guid, AccountAccess>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        await db.UserRoleAssignments.AsNoTracking().AsSingleQuery()
            .Where(x => userIds.Contains(x.UserId) && x.Role.IsActive)
            .Select(x => new
            {
                x.UserId,
                Role = x.Role.Code,
                Permissions = x.Role.RolePermissions.Where(grant => grant.Permission.IsActive)
                    .Select(grant => grant.Permission.Code).ToArray()
            })
            .ToDictionaryAsync(x => x.UserId, x => new AccountAccess(x.Role, x.Permissions), ct);

    public async Task GrantResidentAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var roleId = await db.Roles.Where(r => r.Code == SystemRoleCodes.Resident && r.IsActive).Select(r => r.Id).SingleAsync(ct);
        var existing = await db.UserRoleAssignments.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (existing != null)
        {
            if (existing.RoleId != roleId) throw new InvalidOperationException("Resident account has an incompatible role assignment.");
            return;
        }
        db.UserRoleAssignments.Add(new UserRoleAssignment(userId, roleId, now));
        db.UserAccessHistories.Add(new UserAccessHistory(userId, AccessActionType.ROLE_CHANGED, now,
            newRoleId: roleId, reason: "Resident eligibility verified during account activation."));
        await db.SaveChangesAsync(ct);
    }

    public Task<Guid[]> GetResidentUserIdsAsync(Guid[] userIds, CancellationToken ct) =>
        db.UserRoleAssignments.Where(x => userIds.Contains(x.UserId) && x.Role.IsActive && x.Role.Code == SystemRoleCodes.Resident)
            .Select(x => x.UserId).ToArrayAsync(ct);
}
