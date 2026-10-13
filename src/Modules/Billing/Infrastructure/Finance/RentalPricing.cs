using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Domain.FeeRateRules;
using PropFlow.Modules.Billing.Domain.FeeTypes;
using PropFlow.Modules.Billing.Infrastructure.Persistence;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;
public sealed class RentalPricing(BillingDbContext db, IBillingHouseholdDirectory directory) : IRentalPricing
{
    public const string FeeCode = "APARTMENT_RENT";
    public async Task<IReadOnlyList<RentalPricingView>> ListAsync(Guid? apartmentId, CancellationToken ct) =>
        await db.FeeRateRules.AsNoTracking().Where(x => x.FeeType!.Code == FeeCode && x.IsActive && (x.ApartmentUnitId != null || x.ApartmentTypeId != null)
            && (apartmentId == null || x.ApartmentUnitId == apartmentId)).OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new RentalPricingView(x.Id, x.ApartmentUnitId ?? Guid.Empty, x.BillingFrequencyCode, x.UnitRate, x.EffectiveFrom, x.EffectiveTo, x.ApartmentTypeId)).ToArrayAsync(ct);

    public async Task<IReadOnlyList<RentalPricingView>> ListForTypeAsync(Guid typeId, CancellationToken ct) =>
        (await ListAsync(null, ct)).Where(x => x.ApartmentTypeId == typeId).ToArray();

    public async Task SaveAsync(RentalPricingInput input, Guid actor, CancellationToken ct)
    {
        if (input.Basis is not ("MONTHLY" or "DAILY") || input.UnitRate < 0 || input.UnitRate >= 1000000000000000000m
            || input.UnitRate != decimal.Round(input.UnitRate, 0))
            throw new ArgumentException("Chọn MONTHLY hoặc DAILY và nhập số tiền VND nguyên không âm, trong giới hạn numeric(18,0).");
        var managed = await directory.ManagedApartmentsAsync(ct);
        if (input.ApartmentTypeId is not null && input.ApartmentUnitId != Guid.Empty) throw new ArgumentException("Chỉ chọn một phạm vi giá thuê.");
        if (!managed.Any(x => input.ApartmentTypeId is Guid typeId ? x.ApartmentTypeId == typeId : x.Id == input.ApartmentUnitId))
            throw new ArgumentException("Căn hộ không thuộc phạm vi quản lý đang hoạt động của hệ thống.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241002)", ct);
        var fee = await db.FeeTypes.SingleOrDefaultAsync(x => x.Code == FeeCode, ct);
        if (fee is null) { fee = new FeeType(FeeCode, "Tiền thuê căn hộ", "FIXED", "MONTHLY", actor, DateTimeOffset.UtcNow); db.FeeTypes.Add(fee); }
        var versions = await db.FeeRateRules.Where(x => x.FeeTypeId == fee.Id && (input.ApartmentTypeId != null ? x.ApartmentTypeId == input.ApartmentTypeId && x.BillingFrequencyCode == input.Basis : x.ApartmentUnitId == input.ApartmentUnitId) && x.IsActive).ToArrayAsync(ct);
        var monthStart = new DateOnly(input.EffectiveFrom.Year, input.EffectiveFrom.Month, 1);
        if (input.EffectiveFrom.Day != 1 && versions.Any(x => x.BillingFrequencyCode != input.Basis
            && x.EffectiveFrom < input.EffectiveFrom && (x.EffectiveTo == null || x.EffectiveTo >= monthStart)))
            throw new ArgumentException("Không dùng đồng thời giá tháng và giá ngày trong cùng kỳ. Đổi cơ sở tính từ đầu tháng tiếp theo.");
        if (versions.Any(x => x.EffectiveFrom > input.EffectiveFrom)) throw new ArgumentException("Không chèn đơn giá trước phiên bản tương lai đã có; hãy cập nhật đúng phiên bản hoặc thêm phiên bản sau.");
        var same = versions.SingleOrDefault(x => x.EffectiveFrom == input.EffectiveFrom);
        var versionIds = versions.Select(v => v.Id).ToArray();
        var issued = await db.InvoiceItems.AnyAsync(x => x.FeeRateRuleId != null && versionIds.Contains(x.FeeRateRuleId.Value)
            && db.Invoices.Any(i => i.Id == x.InvoiceId && i.Status != PropFlow.Modules.Billing.Domain.Invoices.InvoiceStatus.DRAFT
                && i.BillingPeriodEnd >= input.EffectiveFrom), ct);
        if (issued) throw new ArgumentException("Kỳ này đã có chứng từ phát hành. Đặt phiên bản sau kỳ đã phát hành để giữ nguyên lịch sử.");
        var now = DateTimeOffset.UtcNow;
        if (same is not null) same.UpdateRule("Tiền thuê căn hộ", "FIXED", input.Basis, input.UnitRate, input.EffectiveFrom, actor, now, unitName: input.Basis == "DAILY" ? "ngày" : "tháng", effectiveTo: same.EffectiveTo);
        else {
            foreach (var old in versions.Where(x => x.EffectiveTo == null || x.EffectiveTo >= input.EffectiveFrom))
                old.UpdateRule(old.RuleName, old.CalculationMethodCode, old.BillingFrequencyCode, old.UnitRate, old.EffectiveFrom, actor, now,
                    old.UnitName, old.MinimumAmount, old.MaximumAmount, old.RuleConfig, input.EffectiveFrom.AddDays(-1));
            var rule = new FeeRateRule(fee.Id, "Tiền thuê căn hộ", "FIXED", input.Basis, input.UnitRate, input.EffectiveFrom, actor, now, unitName: input.Basis == "DAILY" ? "ngày" : "tháng");
            if (input.ApartmentTypeId is Guid typeId) rule.AssignApartmentType(typeId); else rule.AssignApartment(input.ApartmentUnitId); db.FeeRateRules.Add(rule);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
