using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Domain.UserAccessHistories;
using PropFlow.Modules.Administration.Infrastructure.Persistence;

namespace PropFlow.Modules.Administration.Infrastructure;

public sealed class AccountAccessService(AdministrationDbContext db) : IAccountAccess
{
    public async Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct)
    {
        var role = await db.UserRoleAssignments.AsNoTracking().Where(x => x.UserId == userId && x.Role.IsActive)
            .Select(x => new { x.RoleId, x.Role.Code }).SingleOrDefaultAsync(ct);
        if (role == null) return null;
        var permissions = await db.RolePermissions.Where(x => x.RoleId == role.RoleId && x.Permission.IsActive)
            .Select(x => x.Permission.Code).Distinct().ToArrayAsync(ct);
        return new AccountAccess(role.Code, permissions);
    }

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
}
