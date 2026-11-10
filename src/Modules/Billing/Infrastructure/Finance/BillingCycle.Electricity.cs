using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Domain.ElectricityUsages;
using PropFlow.Modules.Billing.Domain.Invoices;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;
public sealed partial class BillingCycle
{
    public async Task<ElectricityUsageView> ElectricityAsync(Guid apartmentId, DateOnly month, CancellationToken ct)
    {
        await ValidateElectricityApartmentAsync(apartmentId, month, ct);
        var readings = db.ElectricityUsages.AsNoTracking().Where(x => x.ApartmentId == apartmentId);
        var row = await readings.SingleOrDefaultAsync(x => x.UsageMonth == month, ct);
        var previous = readings.Where(x => x.UsageMonth < month);
        return new(apartmentId, month, row?.Kwh, await ElectricityLockedAsync(apartmentId, month, ct), row?.UpdatedAt, row?.WaterM3,
            row?.PreviousElectricityReading ?? await PreviousElectricityAsync(previous, ct), row?.CurrentElectricityReading,
            row?.PreviousWaterReading ?? await PreviousWaterAsync(previous, ct), row?.CurrentWaterReading);
    }
    public Task SaveElectricityAsync(Guid apartmentId, ElectricityUsageInput input, Guid actor, CancellationToken ct)
    {
        if (input.CurrentElectricityReading is null) throw new ArgumentException("Nhập chỉ số điện hiện tại.");
        return SaveUtilitiesAsync(apartmentId, new(input.UsageMonth, input.CurrentElectricityReading, null), actor, ct);
    }
    public async Task SaveUtilitiesAsync(Guid apartmentId, UtilityUsageInput input, Guid actor, CancellationToken ct)
    {
        await ValidateElectricityApartmentAsync(apartmentId, input.UsageMonth, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serialize readings with invoice generation and other reading updates.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241001)", ct);
        if (await ElectricityLockedAsync(apartmentId, input.UsageMonth, ct))
            throw new InvalidOperationException("Kỳ điện, nước đã có hóa đơn. Không thể sửa số liệu của chứng từ đã lập.");
        var readings = db.ElectricityUsages.Where(x => x.ApartmentId == apartmentId);
        var row = await readings.SingleOrDefaultAsync(x => x.UsageMonth == input.UsageMonth, ct);
        var previous = readings.Where(x => x.UsageMonth < input.UsageMonth);
        var now = DateTimeOffset.UtcNow;
        var usage = row ?? new ElectricityUsage(apartmentId, input.UsageMonth, actor, now);
        var later = readings.Where(x => x.UsageMonth > input.UsageMonth);
        if (input.CurrentElectricityReading is { } electricity && electricity != row?.CurrentElectricityReading &&
            await later.AnyAsync(x => x.CurrentElectricityReading != null, ct))
            throw new InvalidOperationException("Không thể đổi chỉ số điện vì đã ghi chỉ số điện của kỳ sau.");
        if (input.CurrentWaterReading is { } water && water != row?.CurrentWaterReading &&
            await later.AnyAsync(x => x.CurrentWaterReading != null, ct))
            throw new InvalidOperationException("Không thể đổi chỉ số nước vì đã ghi chỉ số nước của kỳ sau.");

        usage.RecordReadings(await PreviousElectricityAsync(previous, ct), input.CurrentElectricityReading,
            await PreviousWaterAsync(previous, ct), input.CurrentWaterReading, actor, now);
        if (row is null) db.ElectricityUsages.Add(usage);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task<decimal> PreviousElectricityAsync(IQueryable<ElectricityUsage> previous, CancellationToken ct) =>
        await previous.Where(x => x.CurrentElectricityReading != null).OrderByDescending(x => x.UsageMonth)
            .Select(x => x.CurrentElectricityReading).FirstOrDefaultAsync(ct) ?? 0;

    private static async Task<decimal> PreviousWaterAsync(IQueryable<ElectricityUsage> previous, CancellationToken ct) =>
        await previous.Where(x => x.CurrentWaterReading != null).OrderByDescending(x => x.UsageMonth)
            .Select(x => x.CurrentWaterReading).FirstOrDefaultAsync(ct) ?? 0;
    private async Task ValidateElectricityApartmentAsync(Guid apartmentId, DateOnly month, CancellationToken ct)
    {
        await ConfigureTimeZoneAsync(ct);
        var current = new DateOnly(Today.Year, Today.Month, 1);
        if (month.Day != 1 || month > current || month == DateOnly.MinValue)
            throw new ArgumentException("Chọn tháng sử dụng điện hiện tại hoặc trước đó.");
        if (!(await directory.ManagedApartmentsAsync(ct)).Any(x => x.Id == apartmentId))
            throw new ArgumentException("Không tìm thấy căn hộ được quản lý.");
    }
    private Task<bool> ElectricityLockedAsync(Guid apartmentId, DateOnly month, CancellationToken ct)
    {
        var billMonth = month.AddMonths(1);
        return db.Invoices.AnyAsync(x => x.ApartmentUnitId == apartmentId && x.BillingPeriodStart == billMonth
            && (x.Type == InvoiceType.SERVICE_FEE || x.Type == null && x.Items.Any(i => i.FeeCodeSnapshot != RentalPricing.FeeCode)), ct);
    }
}
