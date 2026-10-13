using System.Text;

namespace PropFlow.Modules.PropertyAssets.Domain.Equipment;

public sealed class EquipmentType
{
    private EquipmentType() { }

    public EquipmentType(string name, Guid? createdBy, DateTimeOffset now)
    {
        Name = NormalizeName(name);
        Id = Guid.NewGuid();
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length is < 1 or > 100)
            throw new ArgumentException("Tên loại thiết bị phải từ 1 đến 100 ký tự.", nameof(name));
        return normalized;
    }
}
