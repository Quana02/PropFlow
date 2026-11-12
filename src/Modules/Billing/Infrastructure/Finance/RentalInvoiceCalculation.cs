using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Domain.FeeRateRules;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;
public static class RentalInvoiceCalculation
{
    public static void Append(Invoice invoice, FeeRateRule rate, string capacity, DateOnly start, DateOnly? end, Guid actor, DateTimeOffset now, Guid? apartmentTypeId = null)
    {
        if (capacity != "RENTAL") return;
        if (rate.ApartmentTypeId is Guid typeId ? typeId != apartmentTypeId : rate.ApartmentUnitId != invoice.ApartmentUnitId) throw new ArgumentException("Rental price belongs to another apartment.");
        var first = start > invoice.BillingPeriodStart ? start : invoice.BillingPeriodStart;
        var last = end is { } e && e < invoice.BillingPeriodEnd ? e : invoice.BillingPeriodEnd;
        if (rate.EffectiveFrom > first) first = rate.EffectiveFrom;
        if (rate.EffectiveTo is { } to && to < last) last = to;
        if (last < first) return;
        var days = last.DayNumber - first.DayNumber + 1;
        var monthly = rate.BillingFrequencyCode == "MONTHLY";
        if (!monthly && rate.BillingFrequencyCode != "DAILY") throw new ArgumentException("Invalid rental basis.");
        var daysInMonth = DateTime.DaysInMonth(invoice.BillingPeriodStart.Year, invoice.BillingPeriodStart.Month);
        var quantity = days;
        if (invoice.Items.Any(x => x.FeeCodeSnapshot == RentalPricing.FeeCode && (x.AmountDivisor > 1) != monthly))
            throw new InvalidOperationException("An invoice cannot mix monthly and daily rental bases.");
        var existing = invoice.Items.SingleOrDefault(x => x.FeeCodeSnapshot == RentalPricing.FeeCode && x.FeeRateRuleId == rate.Id);
        if (existing is not null) {
            if (existing.FeeRateRuleId == rate.Id && existing.Quantity == quantity) return;
            throw new InvalidOperationException("An invoice cannot contain conflicting rental bases.");
        }
        invoice.AddItem(RentalPricing.FeeCode, monthly ? "Tiền thuê theo tháng" : "Tiền thuê theo ngày", monthly ? "PRORATED_MONTHLY" : "FIXED", quantity,
            rate.UnitRate, actor, now, rate.FeeTypeId, rate.Id,
            description: $"{first:dd/MM/yyyy}–{last:dd/MM/yyyy}, {days} ngày; giá {rate.UnitRate:N0} VND/{(monthly ? "tháng" : "ngày")}" + (monthly ? $"; chia theo {daysInMonth} ngày trong tháng" : ""), unitName: "ngày", amountDivisor: monthly ? daysInMonth : 1);
    }
}
