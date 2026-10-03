namespace PropFlow.Modules.Apartments.Domain.ApartmentUnits;

public sealed class ApartmentUnitType
{
    private ApartmentUnitType() { }

    public ApartmentUnitType(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > 80) throw new ArgumentOutOfRangeException(nameof(name));
        Id = Guid.NewGuid();
        Name = trimmed;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > 80) throw new ArgumentOutOfRangeException(nameof(name));
        Name = trimmed;
        UpdatedAt = now;
    }
}
