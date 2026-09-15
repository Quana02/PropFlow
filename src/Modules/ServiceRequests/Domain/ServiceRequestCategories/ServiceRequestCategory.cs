using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories;

public class ServiceRequestCategory
{
    private readonly List<ServiceRequest> _serviceRequests = [];

    private ServiceRequestCategory()
    {
    }

    public ServiceRequestCategory(
        string code,
        string name,
        DateTimeOffset now,
        string? description = null,
        int displayOrder = 0,
        Guid? createdBy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = true;
        DisplayOrder = displayOrder;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ServiceRequest> ServiceRequests => _serviceRequests.AsReadOnly();

    public void Update(string name, string? description, int displayOrder, Guid? updatedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DisplayOrder = displayOrder;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Activate(Guid? updatedBy, DateTimeOffset now)
    {
        IsActive = true;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Deactivate(Guid? updatedBy, DateTimeOffset now)
    {
        IsActive = false;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
