namespace PropFlow.Modules.Apartments.Domain.ApartmentUnits;

public sealed class ApartmentUnitType
{
    private ApartmentUnitType() { }

    public ApartmentUnitType(string name, DateTimeOffset now, decimal? defaultUsableAreaM2 = null,
        int? defaultBedroomCount = null, int? defaultBathroomCount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > 80) throw new ArgumentOutOfRangeException(nameof(name));
        Id = Guid.NewGuid();
        Name = trimmed;
        CreatedAt = now;
        UpdatedAt = now;
        UpdateDefaults(defaultUsableAreaM2, defaultBedroomCount, defaultBathroomCount, now);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal? DefaultUsableAreaM2 { get; private set; }
    public int? DefaultBedroomCount { get; private set; }
    public int? DefaultBathroomCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateDefaults(decimal? area, int? bedrooms, int? bathrooms, DateTimeOffset now)
    {
        if (area.HasValue && (area.Value < 25 || area.Value > 99999999.99m || decimal.Round(area.Value, 2) != area.Value))
            throw new ArgumentException("Diện tích mặc định phải từ 25 m² đến 99.999.999,99 m² và không quá 2 chữ số thập phân.");
        if (bedrooms is < 0 || bathrooms is < 0)
            throw new ArgumentException("Số phòng ngủ và phòng tắm mặc định phải là số nguyên không âm.");
        DefaultUsableAreaM2 = area;
        DefaultBedroomCount = bedrooms;
        DefaultBathroomCount = bathrooms;
        UpdatedAt = now;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > 80) throw new ArgumentOutOfRangeException(nameof(name));
        Name = trimmed;
        UpdatedAt = now;
    }
}
