using System.Globalization;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Domain.PricingChanges;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;
public static class PricingChangeMessages
{
    private static readonly NumberFormatInfo Format = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
    private static string Money(decimal amount) => amount.ToString("N0", Format) + " đ";
    public static PricingChange Service(ServiceRateInput input, Guid actor)
    {
        var amounts = new List<string> { $"Dịch vụ: {Money(input.UnitRate)}/m²/tháng" };
        if (input.WaterM3UnitRate is { } water) amounts.Add($"Nước: {Money(water)}/m³");
        if (input.WaterUnitRate is { } person) amounts.Add($"Nước: {Money(person)}/người/tháng");
        if (input.ElectricityUnitRate is { } electricity) amounts.Add($"Điện: {Money(electricity)}/kWh");
        return new("Thông báo đơn giá dịch vụ, nước và điện", $"Ban quản lý thông báo đơn giá áp dụng từ {input.EffectiveFrom:dd/MM/yyyy}. {string.Join("; ", amounts)}. Hóa đơn đã phát hành giữ nguyên số tiền.", actor, DateTimeOffset.UtcNow);
    }
    public static PricingChange Rental(RentalPricingInput input, string scope, Guid actor) =>
        new("Thông báo đơn giá thuê căn hộ", $"Ban quản lý thông báo giá thuê {scope}: {Money(input.UnitRate)}/{(input.Basis == "DAILY" ? "ngày" : "tháng")}, áp dụng từ {input.EffectiveFrom:dd/MM/yyyy}. Chỉ tính tiền thuê cho chủ hộ thuê ở căn chưa có chủ sở hữu. Hóa đơn đã phát hành giữ nguyên số tiền.", actor, DateTimeOffset.UtcNow);
}
