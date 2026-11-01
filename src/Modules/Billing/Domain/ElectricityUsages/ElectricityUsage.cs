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
    {
        if (apartmentId == Guid.Empty || usageMonth.Day != 1) throw new ArgumentException("Căn hộ hoặc kỳ sử dụng không hợp lệ.");
        Id = Guid.NewGuid(); ApartmentId = apartmentId; UsageMonth = usageMonth; CreatedBy = actor; CreatedAt = now;
        UpdateUtilities(kwh, waterM3, actor, now);
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
