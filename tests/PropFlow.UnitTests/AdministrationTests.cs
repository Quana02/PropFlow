using PropFlow.Modules.Administration.Domain.AuditLogs;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.SystemConfigurations;
using PropFlow.Modules.Administration.Domain.UserAccessHistories;
using PropFlow.Modules.Administration.Domain.UserBuildingAccesses;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;

namespace PropFlow.UnitTests;

public class AdministrationTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Role_Constructor_GeneratesId_AndUppercasesCode()
    {
        var role = new Role(" admin ", "Administrator", _now, "System admin role");

        Assert.NotEqual(Guid.Empty, role.Id);
        Assert.Equal("ADMIN", role.Code);
        Assert.Equal("Administrator", role.Name);
        Assert.Equal("System admin role", role.Description);
        Assert.True(role.IsActive);
        Assert.Equal(_now, role.CreatedAt);
        Assert.Equal(_now, role.UpdatedAt);

        Assert.Throws<ArgumentException>(() => new Role("", "Admin", _now));
        Assert.Throws<ArgumentException>(() => new Role("ADMIN", "", _now));
    }

    [Fact]
    public void SystemRoleCodes_ShouldContainExactlyFiveSupportedCodes()
    {
        Assert.Equal(5, SystemRoleCodes.All.Count);
        Assert.Contains(SystemRoleCodes.Resident, SystemRoleCodes.All);
        Assert.Contains(SystemRoleCodes.Staff, SystemRoleCodes.All);
        Assert.Contains(SystemRoleCodes.Accountant, SystemRoleCodes.All);
        Assert.Contains(SystemRoleCodes.Manager, SystemRoleCodes.All);
        Assert.Contains(SystemRoleCodes.Admin, SystemRoleCodes.All);
    }

    [Fact]
    public void Role_Constructor_AcceptsOnlySupportedSystemRoleCodes()
    {
        foreach (var code in SystemRoleCodes.All)
        {
            var role = new Role(code.ToLowerInvariant(), code, _now);

            Assert.Equal(code, role.Code);
        }

        Assert.Throws<ArgumentException>(() => new Role("OWNER", "Owner", _now));
        Assert.Throws<ArgumentException>(() => new Role("SUPER_ADMIN", "Super Admin", _now));
        Assert.Throws<ArgumentException>(() => new Role(null!, "Admin", _now));
        Assert.Throws<ArgumentException>(() => new Role(" ", "Admin", _now));
    }

    [Fact]
    public void Role_ShouldNotExposePublicMethodToChangeCode()
    {
        var publicInstanceMethods = typeof(Role)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(method => method.Name)
            .ToList();

        Assert.DoesNotContain("ChangeCode", publicInstanceMethods);
        Assert.DoesNotContain("UpdateCode", publicInstanceMethods);
        Assert.DoesNotContain("SetCode", publicInstanceMethods);
    }

    [Fact]
    public void Role_UpdateAndToggle_ModifiesState()
    {
        var role = new Role("MANAGER", "Property Manager", _now);
        var updateTime = _now.AddHours(2);

        role.Update("Senior Property Manager", "Lead manager", updateTime);
        Assert.Equal("Senior Property Manager", role.Name);
        Assert.Equal("Lead manager", role.Description);
        Assert.Equal(updateTime, role.UpdatedAt);

        var deactTime = updateTime.AddHours(1);
        role.Deactivate(deactTime);
        Assert.False(role.IsActive);
        Assert.Equal(deactTime, role.UpdatedAt);

        var actTime = deactTime.AddHours(1);
        role.Activate(actTime);
        Assert.True(role.IsActive);
        Assert.Equal(actTime, role.UpdatedAt);
    }

    [Fact]
    public void Permission_Constructor_AndUpdates_WorkProperly()
    {
        var perm = new Permission("ASSETS_VIEW", "View Assets", "PropertyAssets", _now);

        Assert.NotEqual(Guid.Empty, perm.Id);
        Assert.Equal("ASSETS_VIEW", perm.Code);
        Assert.Equal("View Assets", perm.Name);
        Assert.Equal("PropertyAssets", perm.Module);
        Assert.True(perm.IsActive);

        var later = _now.AddDays(1);
        perm.Update("View All Assets", "Assets", "Updated desc", later);
        Assert.Equal("View All Assets", perm.Name);
        Assert.Equal("Assets", perm.Module);
        Assert.Equal(later, perm.UpdatedAt);

        perm.Deactivate(later.AddHours(1));
        Assert.False(perm.IsActive);
    }

    [Fact]
    public void RolePermission_CompositeKey_ValidatesGuids()
    {
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new RolePermission(Guid.Empty, permissionId, _now));
        Assert.Throws<ArgumentException>(() => new RolePermission(roleId, Guid.Empty, _now));

        var rp = new RolePermission(roleId, permissionId, _now);
        Assert.Equal(roleId, rp.RoleId);
        Assert.Equal(permissionId, rp.PermissionId);
        Assert.Equal(_now, rp.CreatedAt);
    }

    [Fact]
    public void UserRoleAssignment_Constructor_AndChangeRole_WorkCorrectly()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new UserRoleAssignment(Guid.Empty, roleId, _now));
        Assert.Throws<ArgumentException>(() => new UserRoleAssignment(userId, Guid.Empty, _now));

        var assignment = new UserRoleAssignment(userId, roleId, _now, actor);
        Assert.NotEqual(Guid.Empty, assignment.Id);
        Assert.Equal(userId, assignment.UserId);
        Assert.Equal(roleId, assignment.RoleId);
        Assert.Equal(actor, assignment.AssignedBy);
        Assert.Equal(_now, assignment.AssignedAt);

        var newRoleId = Guid.NewGuid();
        var changeTime = _now.AddDays(5);
        assignment.ChangeRole(newRoleId, actor, changeTime);
        Assert.Equal(newRoleId, assignment.RoleId);
        Assert.Equal(changeTime, assignment.UpdatedAt);

        Assert.Throws<ArgumentException>(() => assignment.ChangeRole(Guid.Empty, actor, changeTime));
    }

    [Fact]
    public void UserBuildingAccess_Constructor_AndRevoke_WorkCorrectly()
    {
        var userId = Guid.NewGuid();
        var buildingId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new UserBuildingAccess(Guid.Empty, buildingId, _now));
        Assert.Throws<ArgumentException>(() => new UserBuildingAccess(userId, Guid.Empty, _now));

        var access = new UserBuildingAccess(userId, buildingId, _now, actor, "Assigned as manager");
        Assert.NotEqual(Guid.Empty, access.Id);
        Assert.Equal(userId, access.UserId);
        Assert.Equal(buildingId, access.BuildingId);
        Assert.Equal("Assigned as manager", access.Reason);
        Assert.Null(access.RevokedAt);

        var revoker = Guid.NewGuid();
        var revokeTime = _now.AddMonths(1);
        access.Revoke(revoker, revokeTime, "Transferred to other site");
        Assert.Equal(revoker, access.RevokedBy);
        Assert.Equal(revokeTime, access.RevokedAt);
        Assert.Equal("Transferred to other site", access.Reason);

        Assert.Throws<ArgumentException>(() => access.Revoke(Guid.Empty, revokeTime));
    }

    [Fact]
    public void UserAccessHistory_IsAppendOnly_AndSetsProperties()
    {
        var targetUserId = Guid.NewGuid();
        var oldRoleId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new UserAccessHistory(Guid.Empty, AccessActionType.ROLE_CHANGED, _now));

        var history = new UserAccessHistory(
            targetUserId,
            AccessActionType.ROLE_CHANGED,
            _now,
            oldRoleId: oldRoleId,
            newRoleId: newRoleId,
            reason: "Promotion",
            performedBy: actor,
            metadata: "{\"ip\":\"127.0.0.1\"}");

        Assert.NotEqual(Guid.Empty, history.Id);
        Assert.Equal(targetUserId, history.TargetUserId);
        Assert.Equal(AccessActionType.ROLE_CHANGED, history.Action);
        Assert.Equal(oldRoleId, history.OldRoleId);
        Assert.Equal(newRoleId, history.NewRoleId);
        Assert.Equal("Promotion", history.Reason);
        Assert.Equal(actor, history.PerformedBy);
        Assert.Equal("{\"ip\":\"127.0.0.1\"}", history.Metadata);
        Assert.Equal(_now, history.CreatedAt);
    }

    [Fact]
    public void SystemConfiguration_Constructor_AndUpdates_WorkCorrectly()
    {
        Assert.Throws<ArgumentException>(() => new SystemConfiguration("", "value", _now));

        var config = new SystemConfiguration("SECURITY_MAX_LOGIN_ATTEMPTS", "5", _now, ConfigValueType.INTEGER, "Max login tries");
        Assert.NotEqual(Guid.Empty, config.Id);
        Assert.Equal("SECURITY_MAX_LOGIN_ATTEMPTS", config.ConfigKey);
        Assert.Equal("5", config.ConfigValue);
        Assert.Equal(ConfigValueType.INTEGER, config.ValueType);
        Assert.True(config.IsActive);

        var actor = Guid.NewGuid();
        var updateTime = _now.AddDays(2);
        config.UpdateValue("10", actor, updateTime);
        Assert.Equal("10", config.ConfigValue);
        Assert.Equal(updateTime, config.UpdatedAt);
        Assert.Equal(actor, config.UpdatedBy);

        config.ToggleActive(false, actor, updateTime.AddHours(1));
        Assert.False(config.IsActive);
    }

    [Fact]
    public void AuditLog_IsAppendOnly_AndSetsProperties()
    {
        Assert.Throws<ArgumentException>(() => new AuditLog("", "Building", _now));
        Assert.Throws<ArgumentException>(() => new AuditLog("CREATE", "", _now));

        var actor = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var log = new AuditLog(
            "CREATE",
            "Building",
            _now,
            actorUserId: actor,
            entityId: entityId,
            oldValues: null,
            newValues: "{\"Name\":\"Tower A\"}",
            ipAddress: "192.168.1.1",
            correlationId: "corr-123");

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal("CREATE", log.Action);
        Assert.Equal("Building", log.EntityType);
        Assert.Equal(actor, log.ActorUserId);
        Assert.Equal(entityId, log.EntityId);
        Assert.Equal("{\"Name\":\"Tower A\"}", log.NewValues);
        Assert.Equal("192.168.1.1", log.IpAddress);
        Assert.Equal("corr-123", log.CorrelationId);
        Assert.Equal(_now, log.CreatedAt);
    }
}
