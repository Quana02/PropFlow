using PropFlow.Modules.Administration.Domain.Roles;

namespace PropFlow.Modules.Administration.Domain.Permissions;

public class Permission
{
    private readonly List<RolePermission> _rolePermissions = new();

    private Permission()
    {
    }

    public Permission(string code, string name, string module, DateTimeOffset now, string? description = null, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Permission code is required.", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Permission name is required.", nameof(name));

        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("Permission module is required.", nameof(module));

        Id = Guid.NewGuid();
        Code = code.Trim();
        Name = name.Trim();
        Module = module.Trim();
        Description = description?.Trim();
        IsActive = isActive;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Module { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions.AsReadOnly();

    public void Update(string name, string module, string? description, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Permission name is required.", nameof(name));

        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("Permission module is required.", nameof(module));

        Name = name.Trim();
        Module = module.Trim();
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
