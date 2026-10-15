using System.Text;

namespace PropFlow.Modules.PropertyAssets.Domain.Facilities;

public sealed class FacilityType
{
    private FacilityType() { }

    public FacilityType(string name, Guid? createdBy, DateTimeOffset now)
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
        if (normalized.Length is < 1 or > 80)
            throw new ArgumentException("Tên loại cơ sở vật chất phải từ 1 đến 80 ký tự.", nameof(name));
        return normalized;
    }
}
