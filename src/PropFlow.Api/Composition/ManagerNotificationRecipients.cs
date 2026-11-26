using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;

namespace PropFlow.Api.Composition;

public sealed class ManagerNotificationRecipients(AuthenticationDbContext accounts, AdministrationDbContext administration)
    : IManagerNotificationRecipients
{
    public async Task<IReadOnlyList<Guid>> GetUserIdsAsync(CancellationToken ct)
    {
        var reviewers = await administration.UserRoleAssignments.AsNoTracking()
            .Where(x => x.Role.IsActive && x.Role.Code == SystemRoleCodes.Manager &&
                administration.RolePermissions.Any(p => p.RoleId == x.RoleId && p.Permission.IsActive &&
                    p.Permission.Code == SystemPermissionCodes.ManageOperations))
            .Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        return await accounts.UserAccounts.AsNoTracking()
            .Where(x => reviewers.Contains(x.Id) && x.Status == AccountStatus.ACTIVE)
            .Select(x => x.Id).ToArrayAsync(ct);
    }
}
