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
        var row = await db.ElectricityUsages.AsNoTracking().SingleOrDefaultAsync(x => x.ApartmentId == apartmentId && x.UsageMonth == month, ct);
        return new(apartmentId, month, row?.Kwh, await ElectricityLockedAsync(apartmentId, month, ct), row?.UpdatedAt, row?.WaterM3);
    }
    public async Task SaveElectricityAsync(Guid apartmentId, ElectricityUsageInput input, Guid actor, CancellationToken ct)
    {
        await ValidateElectricityApartmentAsync(apartmentId, input.UsageMonth, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // The same lock as invoice generation prevents changing a reading while it is being billed.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241001)", ct);
        if (await ElectricityLockedAsync(apartmentId, input.UsageMonth, ct))
            throw new InvalidOperationException("Kỳ điện này đã gắn với hóa đơn. Không thể sửa số điện của chứng từ đã lập.");
        var row = await db.ElectricityUsages.SingleOrDefaultAsync(x => x.ApartmentId == apartmentId && x.UsageMonth == input.UsageMonth, ct);
        if (row is null) db.ElectricityUsages.Add(new ElectricityUsage(apartmentId, input.UsageMonth, input.Kwh, actor, DateTimeOffset.UtcNow));
        else row.Update(input.Kwh, actor, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task SaveUtilitiesAsync(Guid apartmentId, UtilityUsageInput input, Guid actor, CancellationToken ct)
    {
        await ValidateElectricityApartmentAsync(apartmentId, input.UsageMonth, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241001)", ct);
        if (await ElectricityLockedAsync(apartmentId, input.UsageMonth, ct))
            throw new InvalidOperationException("Kỳ điện, nước đã có hóa đơn. Không thể sửa số liệu của chứng từ đã lập.");
        var row = await db.ElectricityUsages.SingleOrDefaultAsync(x => x.ApartmentId == apartmentId && x.UsageMonth == input.UsageMonth, ct);
        if (row is null) db.ElectricityUsages.Add(new ElectricityUsage(apartmentId, input.UsageMonth, input.Kwh, input.WaterM3, actor, DateTimeOffset.UtcNow));
        else row.UpdateUtilities(input.Kwh, input.WaterM3, actor, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
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
