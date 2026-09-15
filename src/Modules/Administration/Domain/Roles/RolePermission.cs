using PropFlow.Modules.Administration.Domain.Permissions;

namespace PropFlow.Modules.Administration.Domain.Roles;

public class RolePermission
{
    private RolePermission()
    {
    }

    public RolePermission(Guid roleId, Guid permissionId, DateTimeOffset now, Guid? createdBy = null)
    {
        if (roleId == Guid.Empty)
            throw new ArgumentException("RoleId cannot be empty.", nameof(roleId));

        if (permissionId == Guid.Empty)
            throw new ArgumentException("PermissionId cannot be empty.", nameof(permissionId));

        RoleId = roleId;
        PermissionId = permissionId;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Within-module navigation properties
    public Role Role { get; private set; } = null!;
    public Permission Permission { get; private set; } = null!;
}
