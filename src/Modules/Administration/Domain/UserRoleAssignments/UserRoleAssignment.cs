using PropFlow.Modules.Administration.Domain.Roles;

namespace PropFlow.Modules.Administration.Domain.UserRoleAssignments;

public class UserRoleAssignment
{
    private UserRoleAssignment()
    {
    }

    public UserRoleAssignment(Guid userId, Guid roleId, DateTimeOffset now, Guid? assignedBy = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));

        if (roleId == Guid.Empty)
            throw new ArgumentException("RoleId cannot be empty.", nameof(roleId));

        Id = Guid.NewGuid();
        UserId = userId;
        RoleId = roleId;
        AssignedBy = assignedBy;
        AssignedAt = now;
        UpdatedBy = assignedBy;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? AssignedBy { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Within-module navigation
    public Role Role { get; private set; } = null!;

    public void ChangeRole(Guid newRoleId, Guid? updatedBy, DateTimeOffset now)
    {
        if (newRoleId == Guid.Empty)
            throw new ArgumentException("New role ID cannot be empty.", nameof(newRoleId));

        RoleId = newRoleId;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
