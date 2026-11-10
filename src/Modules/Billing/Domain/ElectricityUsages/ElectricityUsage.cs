namespace PropFlow.Modules.Billing.Domain.ElectricityUsages;

public sealed class ElectricityUsage
{
    private ElectricityUsage() { }
    public ElectricityUsage(Guid apartmentId, DateOnly usageMonth, decimal kwh, Guid actor, DateTimeOffset now)
    {
        if (apartmentId == Guid.Empty) throw new ArgumentException("Căn hộ không hợp lệ.");
        if (usageMonth.Day != 1) throw new ArgumentException("Kỳ điện phải là đầu tháng.");
        Id = Guid.NewGuid(); ApartmentId = apartmentId; UsageMonth = usageMonth; CreatedBy = actor; CreatedAt = now;
        Update(kwh, actor, now);
    }
    public ElectricityUsage(Guid apartmentId, DateOnly usageMonth, decimal? kwh, decimal? waterM3, Guid actor, DateTimeOffset now)
        : this(apartmentId, usageMonth, actor, now)
    {
        UpdateUtilities(kwh, waterM3, actor, now);
    }

    internal ElectricityUsage(Guid apartmentId, DateOnly usageMonth, Guid actor, DateTimeOffset now)
    {
        if (apartmentId == Guid.Empty || usageMonth.Day != 1) throw new ArgumentException("Căn hộ hoặc kỳ sử dụng không hợp lệ.");
        if (actor == Guid.Empty) throw new ArgumentException("Người cập nhật không hợp lệ.");
        Id = Guid.NewGuid(); ApartmentId = apartmentId; UsageMonth = usageMonth; CreatedBy = actor; CreatedAt = now;
    }

    internal void RecordReadings(decimal previousElectricity, decimal? electricity, decimal previousWater, decimal? water,
        Guid actor, DateTimeOffset now)
    {
        if (electricity is null && water is null) throw new ArgumentException("Nhập ít nhất một chỉ số công tơ điện hoặc nước.");
        var kwh = Consumption(previousElectricity, electricity, "Chỉ số điện (kWh)");
        var waterM3 = Consumption(previousWater, water, "Chỉ số nước (m³)");
        UpdateUtilities(kwh ?? Kwh, waterM3 ?? WaterM3, actor, now);
        if (electricity.HasValue)
        {
            PreviousElectricityReading = previousElectricity;
            CurrentElectricityReading = electricity;
        }
        if (water.HasValue)
        {
            PreviousWaterReading = previousWater;
            CurrentWaterReading = water;
        }
    }

    private static decimal? Consumption(decimal previous, decimal? current, string label)
    {
        Validate(current, label);
        if (current < previous) throw new ArgumentException($"{label} hiện tại không được nhỏ hơn chỉ số trước ({previous:0.###}).");
        return current - previous;
    }
    public void UpdateUtilities(decimal? kwh, decimal? waterM3, Guid actor, DateTimeOffset now)
    {
        if (actor == Guid.Empty) throw new ArgumentException("Người cập nhật không hợp lệ.");
        Validate(kwh, "Số điện (kWh)"); Validate(waterM3, "Số nước (m³)");
        if (kwh is null && waterM3 is null) throw new ArgumentException("Nhập ít nhất số điện hoặc số nước đã kiểm tra.");
        Kwh = kwh; WaterM3 = waterM3; UpdatedBy = actor; UpdatedAt = now;
    }
    private static void Validate(decimal? value, string label)
    {
        if (value is { } number && (number < 0 || number > 1000000 || number != decimal.Round(number, 3)))
            throw new ArgumentException($"{label} phải từ 0 đến 1.000.000, tối đa 3 chữ số thập phân.");
    }
    public Guid Id { get; private set; }
    public Guid ApartmentId { get; private set; }
    public DateOnly UsageMonth { get; private set; }
    public decimal? Kwh { get; private set; }
    public decimal? WaterM3 { get; private set; }
    public decimal? PreviousElectricityReading { get; private set; }
    public decimal? CurrentElectricityReading { get; private set; }
    public decimal? PreviousWaterReading { get; private set; }
    public decimal? CurrentWaterReading { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public void Update(decimal kwh, Guid actor, DateTimeOffset now)
    {
        if (kwh < 0 || kwh > 1000000 || kwh != decimal.Round(kwh, 3)) throw new ArgumentException("Số điện phải từ 0 đến 1.000.000 kWh, tối đa 3 chữ số thập phân.");
        if (actor == Guid.Empty) throw new ArgumentException("Người cập nhật không hợp lệ.");
        Kwh = kwh; UpdatedBy = actor; UpdatedAt = now;
    }
}
