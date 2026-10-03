using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Administration.Infrastructure;

public sealed class MaintenanceStaffDirectory(
    AdministrationDbContext db,
    IInternalAccountDirectory accounts) : IMaintenanceStaffDirectory
{
    public async Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct)
    {
        var staffIds = await StaffUserIdsAsync(ct);
        if (staffIds.Length == 0) return [];

        var identities = await accounts.GetAsync(staffIds, ct);
        return identities
            .Where(account => string.Equals(account.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(account => account.DisplayName)
            .ThenBy(account => account.Username)
            .Select(account => new MaintenanceStaffRecord(account.Id, account.Username, account.DisplayName))
            .ToArray();
    }

    public async Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return null;

        var isStaff = await db.UserRoleAssignments.AsNoTracking()
            .Include(assignment => assignment.Role)
            .AnyAsync(assignment => assignment.UserId == userId && assignment.Role.Code == SystemRoleCodes.Staff, ct);
        if (!isStaff) return null;

        var identity = (await accounts.GetAsync([userId], ct)).SingleOrDefault();
        return identity is null || !string.Equals(identity.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            ? null
            : new MaintenanceStaffRecord(identity.Id, identity.Username, identity.DisplayName);
    }

    public async Task<IReadOnlyList<MaintenanceStaffRecord>> GetHistoricalStaffAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];

        return (await accounts.GetAsync(userIds.Distinct().ToArray(), ct))
            .OrderBy(account => account.DisplayName)
            .ThenBy(account => account.Username)
            .Select(account => new MaintenanceStaffRecord(account.Id, account.Username, account.DisplayName))
            .ToArray();
    }

    private Task<Guid[]> StaffUserIdsAsync(CancellationToken ct) => db.UserRoleAssignments.AsNoTracking()
        .Include(assignment => assignment.Role)
        .Where(assignment => assignment.Role.Code == SystemRoleCodes.Staff)
        .Select(assignment => assignment.UserId)
        .ToArrayAsync(ct);
}
