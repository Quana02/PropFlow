using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceStaffDirectoryTests
{
    [Fact]
    public async Task GetActiveStaffAsync_ReturnsOnlyActiveStaffWithApprovedFields()
    {
        await using var db = CreateDb();
        var staffRole = AddRole(db, SystemRoleCodes.Staff);
        var managerRole = AddRole(db, SystemRoleCodes.Manager);
        var activeStaff = Guid.NewGuid();
        var disabledStaff = Guid.NewGuid();
        var manager = Guid.NewGuid();
        AddRoleAssignment(db, activeStaff, staffRole.Id);
        AddRoleAssignment(db, disabledStaff, staffRole.Id);
        AddRoleAssignment(db, manager, managerRole.Id);
        await db.SaveChangesAsync();
        var directory = new MaintenanceStaffDirectory(db, new Accounts(
            new(activeStaff, "staff.active", "Nhân viên hoạt động", null, "ACTIVE"),
            new(disabledStaff, "staff.disabled", "Nhân viên bị khóa", null, "DISABLED"),
            new(manager, "manager", "Quản lý", null, "ACTIVE")));

        var staff = await directory.GetActiveStaffAsync(default);

        var item = Assert.Single(staff);
        Assert.Equal(activeStaff, item.UserId);
        Assert.Equal("staff.active", item.Username);
        Assert.Equal("Nhân viên hoạt động", item.DisplayName);
        Assert.Null(await directory.GetActiveStaffAsync(manager, default));
        Assert.Null(await directory.GetActiveStaffAsync(disabledStaff, default));
        Assert.Null(await directory.GetActiveStaffAsync(Guid.NewGuid(), default));
    }

    private static AdministrationDbContext CreateDb() => new(new DbContextOptionsBuilder<AdministrationDbContext>()
        .UseInMemoryDatabase($"maintenance-staff-directory-{Guid.NewGuid()}").Options);

    private static Role AddRole(AdministrationDbContext db, string code)
    {
        var role = new Role(code, code, DateTimeOffset.UtcNow);
        db.Roles.Add(role);
        return role;
    }

    private static void AddRoleAssignment(AdministrationDbContext db, Guid userId, Guid roleId) =>
        db.UserRoleAssignments.Add(new UserRoleAssignment(userId, roleId, DateTimeOffset.UtcNow));

    private sealed class Accounts(params InternalAccountRecord[] accounts) : IInternalAccountDirectory
    {
        public Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InternalAccountRecord>>(accounts.Where(account => userIds.Contains(account.Id)).ToArray());
        public Task<PagedInternalAccountRecords> SearchAsync(IReadOnlyCollection<Guid> userIds, string? search, string? status, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
    }
}
