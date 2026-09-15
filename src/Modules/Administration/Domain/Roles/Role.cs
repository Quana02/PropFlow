using PropFlow.Modules.Administration.Domain.UserRoleAssignments;

namespace PropFlow.Modules.Administration.Domain.Roles;

public class Role
{
    private readonly List<RolePermission> _rolePermissions = new();
    private readonly List<UserRoleAssignment> _userRoleAssignments = new();

    private Role()
    {
    }

    public Role(string code, string name, DateTimeOffset now, string? description = null, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name is required.", nameof(name));

        var normalizedCode = SystemRoleCodes.Normalize(code);
        if (!SystemRoleCodes.IsSupported(normalizedCode))
            throw new ArgumentException("Role code is not supported by the system role catalog.", nameof(code));

        Id = Guid.NewGuid();
        Code = normalizedCode;
        Name = name.Trim();
        Description = description?.Trim();
        IsActive = isActive;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions.AsReadOnly();
    public IReadOnlyCollection<UserRoleAssignment> UserRoleAssignments => _userRoleAssignments.AsReadOnly();

    public void Update(string name, string? description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name is required.", nameof(name));

        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
