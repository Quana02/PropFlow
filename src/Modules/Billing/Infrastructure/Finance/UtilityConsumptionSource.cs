using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Persistence;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;

public sealed class UtilityConsumptionSource(BillingDbContext db, IBillingHouseholdDirectory directory) : IUtilityConsumptionSource
{
    private Task<IReadOnlyList<BillingApartment>>? apartments;
    public Task<IReadOnlyList<BillingApartment>> ApartmentsAsync(CancellationToken ct) => apartments ??= directory.ManagedApartmentsAsync(ct);
    public async Task<UtilityConsumptionWindow?> ReadAsync(Guid apartmentId, DateOnly month, CancellationToken ct)
    {
        var apartment = (await ApartmentsAsync(ct)).SingleOrDefault(x => x.Id == apartmentId);
        if (apartment is null)
        {
            var historical = await directory.DescribeAsync(apartmentId, month.AddMonths(1), ct);
            if (historical is null) return null;
            apartment = new(apartmentId, historical.UnitNumber);
        }
        var historyStart = month.AddMonths(-25);
        var rows = await db.ElectricityUsages.AsNoTracking().Where(x => x.ApartmentId == apartmentId && x.UsageMonth >= historyStart && x.UsageMonth <= month)
            .OrderBy(x => x.UsageMonth).ToArrayAsync(ct);
        var bills = await (from i in db.Invoices.AsNoTracking()
            where i.ApartmentUnitId == apartmentId && i.Status == InvoiceStatus.ISSUED
                && (i.Type == InvoiceType.SERVICE_FEE || i.Type == null && i.Items.Any(x => x.FeeCodeSnapshot != RentalPricing.FeeCode))
                && i.BillingPeriodStart >= historyStart.AddMonths(1) && i.BillingPeriodStart <= month.AddMonths(1)
            select new { i.Id, i.BillingPeriodStart }).ToArrayAsync(ct);
        var ids = bills.Select(x => x.Id).ToArray();
        var recipients = await db.InvoiceRecipients.AsNoTracking().Where(x => ids.Contains(x.InvoiceId) && x.UserId != null)
            .Select(x => new { x.InvoiceId, UserId = x.UserId!.Value }).ToArrayAsync(ct);
        var points = rows.Where(x => x.UsageMonth >= month.AddMonths(-24)).Select(row =>
        {
            var previous = rows.SingleOrDefault(x => x.UsageMonth == row.UsageMonth.AddMonths(-1));
            var monthBills = bills.Where(x => x.BillingPeriodStart == row.UsageMonth.AddMonths(1)).Select(x => x.Id).ToHashSet();
            return new UtilityConsumptionPoint(row.UsageMonth, row.Kwh, row.WaterM3,
                row.Kwh.HasValue && (row.CurrentElectricityReading is null || previous?.CurrentElectricityReading is { } e && row.PreviousElectricityReading == e && row.Kwh == row.CurrentElectricityReading - e),
                row.WaterM3.HasValue && (row.CurrentWaterReading is null || previous?.CurrentWaterReading is { } w && row.PreviousWaterReading == w && row.WaterM3 == row.CurrentWaterReading - w),
                recipients.Where(x => monthBills.Contains(x.InvoiceId)).Select(x => x.UserId).Distinct().Order().ToArray(), monthBills.Count > 0);
        }).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(points)));
        return new(apartmentId, apartment.UnitNumber, month, points, fingerprint);
    }
    public async Task<IReadOnlyList<UtilityConsumptionPeriod>> ResidentPeriodsAsync(Guid userId, CancellationToken ct) =>
        await (from i in db.Invoices.AsNoTracking() join r in db.InvoiceRecipients.AsNoTracking() on i.Id equals r.InvoiceId
            where r.UserId == userId && i.Status == InvoiceStatus.ISSUED
                && (i.Type == InvoiceType.SERVICE_FEE || i.Type == null && i.Items.Any(x => x.FeeCodeSnapshot != RentalPricing.FeeCode))
            select new UtilityConsumptionPeriod(i.ApartmentUnitId, i.BillingPeriodStart.AddMonths(-1))).Distinct().ToArrayAsync(ct);
}
