using System.Text;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.PropertyAssets.Contracts;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Domain.FeeTypes;
using PropFlow.Modules.Billing.Domain.FeeRateRules;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Contracts;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;
public sealed partial class BillingCycle(BillingDbContext db, IBillingHouseholdDirectory directory,
    IConfirmedPaymentSource totals, IPaymentNoticeSource payments, IFinanceNoticePublisher notices, TimeProvider? clock = null, ICurrentBuildingTimeZone? buildingTimeZone = null, ILogger<BillingCycle>? logger = null) : IBillingCycle
{
    public const string ServiceFeeCode = "SERVICE_AREA_MONTHLY";
    public const string WaterFeeCode = "WATER_PERSON_MONTHLY";
    public const string WaterVolumeFeeCode = "WATER_M3_MONTHLY";
    public const string ElectricityFeeCode = "ELECTRICITY_KWH_MONTHLY";
    public async Task<InvoiceDetail?> DetailAsync(Guid id, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(x => x.Items).Include(x => x.StatusHistory)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (invoice is null) return null;
        var recipient = await DocumentRecipientAsync(invoice, null, ct);
        var paid = await totals.TotalAsync(id, ct);
        var outstanding = Math.Max(0, invoice.TotalAmount - paid);
        var settlement = invoice.Status != InvoiceStatus.ISSUED ? "NOT_APPLICABLE" : outstanding == 0 ? "PAID" : paid > 0 ? "PARTIAL" : "UNPAID";
        return new(invoice.Id, invoice.InvoiceNumber, invoice.ApartmentUnitId, recipient?.UnitNumber ?? "Chưa có thông tin căn hộ",
            invoice.BillingPeriodStart, invoice.BillingPeriodEnd, invoice.IssueDate, invoice.DueDate, invoice.Status.ToString(),
            invoice.TotalAmount, paid, outstanding, settlement, invoice.CreatedAt, invoice.CreatedBy, invoice.IssuedAt, invoice.IssuedBy, invoice.Note,
            invoice.Items.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new InvoiceDetailLine(x.Id, x.FeeTypeId, x.FeeRateRuleId,
                x.FeeCodeSnapshot, x.FeeNameSnapshot, x.CalculationMethodCodeSnapshot, x.Quantity, x.UnitName, x.UnitRate, x.LineAmount, x.Description, x.AmountDivisor)).ToArray(),
            invoice.StatusHistory.Select(x => new InvoiceDetailHistory(x.FromStatus?.ToString(), x.ToStatus.ToString(), x.ChangedBy, x.ChangedAt, x.Reason)).ToArray(),
            recipient?.FullName, invoice.Type?.ToString());
    }
    private TimeZoneInfo businessZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime((clock ?? TimeProvider.System).GetUtcNow(), businessZone).DateTime);
    private async Task ConfigureTimeZoneAsync(CancellationToken ct)
    {
        if (buildingTimeZone is not null) businessZone = TimeZoneInfo.FindSystemTimeZoneById(await buildingTimeZone.GetAsync(ct));
    }
    public async Task<IReadOnlyList<ServiceRateView>> RatesAsync(CancellationToken ct) => await db.FeeRateRules.AsNoTracking()
        .Where(x => x.FeeType!.Code == ServiceFeeCode && x.IsActive).OrderByDescending(x => x.EffectiveFrom)
        .Select(x => new ServiceRateView(x.Id, x.UnitRate, x.EffectiveFrom, x.CreatedBy,
            db.FeeRateRules.Where(w => w.FeeType!.Code == WaterFeeCode && w.IsActive
                && w.EffectiveFrom <= x.EffectiveFrom && (w.EffectiveTo == null || w.EffectiveTo >= x.EffectiveFrom))
                .OrderByDescending(w => w.EffectiveFrom).Select(w => (decimal?)w.UnitRate).FirstOrDefault(),
            db.FeeRateRules.Where(e => e.FeeType!.Code == ElectricityFeeCode && e.IsActive
                && e.EffectiveFrom <= x.EffectiveFrom && (e.EffectiveTo == null || e.EffectiveTo >= x.EffectiveFrom))
                .OrderByDescending(e => e.EffectiveFrom).Select(e => (decimal?)e.UnitRate).FirstOrDefault(),
            db.FeeRateRules.Where(w => w.FeeType!.Code == WaterVolumeFeeCode && w.IsActive && w.EffectiveFrom <= x.EffectiveFrom && (w.EffectiveTo == null || w.EffectiveTo >= x.EffectiveFrom))
                .OrderByDescending(w => w.EffectiveFrom).Select(w => (decimal?)w.UnitRate).FirstOrDefault())).ToArrayAsync(ct);
    public async Task SaveRateAsync(ServiceRateInput input, Guid actor, CancellationToken ct)
    {
        await ConfigureTimeZoneAsync(ct);
        if (input.UnitRate <= 0 || input.UnitRate > 1000000 || input.UnitRate != decimal.Round(input.UnitRate, 0) || input.EffectiveFrom.Day != 1)
            throw new ArgumentException("Đơn giá phải từ 1 đến 1.000.000 VND/m²; ngày hiệu lực phải là đầu tháng.");
        if (input.WaterUnitRate is { } waterRate && (waterRate < 0 || waterRate >= 1000000000000000000m || waterRate != decimal.Round(waterRate, 0)))
            throw new ArgumentException("Đơn giá nước phải là số tiền VND nguyên không âm, trong giới hạn numeric(18,0).");
        if (input.ElectricityUnitRate is { } electric && (electric <= 0 || electric > 1000000 || electric != decimal.Round(electric, 0)))
            throw new ArgumentException("Đơn giá điện phải từ 1 đến 1.000.000 VND/kWh và là số tiền VND nguyên.");
        if (input.WaterM3UnitRate is { } waterM3 && (waterM3 <= 0 || waterM3 > 1000000 || waterM3 != decimal.Round(waterM3, 0)))
            throw new ArgumentException("Đơn giá nước phải từ 1 đến 1.000.000 VND/m³ và là số tiền VND nguyên.");
        if (input.WaterM3UnitRate is not null && input.ElectricityUnitRate is null)
            throw new ArgumentException("Cấu hình dịch vụ + nước m³ + điện phải có đơn giá điện VND/kWh; không được bỏ tiền điện.");
        if (input.WaterM3UnitRate is not null && input.WaterUnitRate is not null) throw new ArgumentException("Chỉ chọn một cơ sở tính nước trong cùng cấu hình.");
        var month = new DateOnly(Today.Year, Today.Month, 1);
        if (input.EffectiveFrom < month) throw new ArgumentException("Không được thay đổi đơn giá cho kỳ đã qua.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241001)", ct);
        var fee = await db.FeeTypes.SingleOrDefaultAsync(x => x.Code == ServiceFeeCode, ct);
        if (fee is null) { fee = new FeeType(ServiceFeeCode, "Phí dịch vụ chung cư", "AREA", "MONTHLY", actor, DateTimeOffset.UtcNow); db.FeeTypes.Add(fee); }
        if (await db.FeeRateRules.AnyAsync(x => x.FeeTypeId == fee.Id && x.IsActive && x.EffectiveFrom == input.EffectiveFrom, ct))
            throw new ArgumentException("Kỳ này đã có đơn giá. Hãy đặt phiên bản cho tháng tiếp theo.");
        db.FeeRateRules.Add(new FeeRateRule(fee.Id, $"Phí dịch vụ từ {input.EffectiveFrom:MM/yyyy}", "AREA", "MONTHLY",
            input.UnitRate, input.EffectiveFrom, actor, DateTimeOffset.UtcNow, unitName: "m²"));
        if (input.WaterUnitRate is { } waterAmount)
        {
            var water = await db.FeeTypes.SingleOrDefaultAsync(x => x.Code == WaterFeeCode, ct);
            if (water is null) { water = new FeeType(WaterFeeCode, "Nước sinh hoạt", "PERSON", "MONTHLY", actor, DateTimeOffset.UtcNow); db.FeeTypes.Add(water); }
            db.FeeRateRules.Add(new FeeRateRule(water.Id, $"Nước theo người từ {input.EffectiveFrom:MM/yyyy}", "PERSON", "MONTHLY",
                waterAmount, input.EffectiveFrom, actor, DateTimeOffset.UtcNow, unitName: "người"));
        }
        if (input.WaterM3UnitRate is { } waterM3Amount)
        {
            var water = await db.FeeTypes.SingleOrDefaultAsync(x => x.Code == WaterVolumeFeeCode, ct);
            if (water is null) { water = new FeeType(WaterVolumeFeeCode, "Nước sinh hoạt", "VOLUME", "MONTHLY", actor, DateTimeOffset.UtcNow); db.FeeTypes.Add(water); }
            db.FeeRateRules.Add(new FeeRateRule(water.Id, $"Nước theo m³ từ {input.EffectiveFrom:MM/yyyy}", "VOLUME", "MONTHLY",
                waterM3Amount, input.EffectiveFrom, actor, DateTimeOffset.UtcNow, unitName: "m³"));
        }
        if (input.ElectricityUnitRate is { } electricityAmount)
        {
            var electricity = await db.FeeTypes.SingleOrDefaultAsync(x => x.Code == ElectricityFeeCode, ct);
            if (electricity is null) { electricity = new FeeType(ElectricityFeeCode, "Tiền điện", "KWH", "MONTHLY", actor, DateTimeOffset.UtcNow); db.FeeTypes.Add(electricity); }
            db.FeeRateRules.Add(new FeeRateRule(electricity.Id, $"Điện từ {input.EffectiveFrom:MM/yyyy}", "KWH", "MONTHLY",
                electricityAmount, input.EffectiveFrom, actor, DateTimeOffset.UtcNow, unitName: "kWh"));
        }
        db.PricingChanges.Add(PricingChangeMessages.Service(input, actor));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task RunAsync(CancellationToken ct)
    {
        await ConfigureTimeZoneAsync(ct);
        var today = Today; var month = new DateOnly(today.Year, today.Month, 1);
        await GenerateAsync(month, ct);
        await CaptureExistingRecipientsAsync(ct);
        await ReconcileAsync(null, ct);
    }
    public async Task NotifyPaymentAsync(Guid invoiceId, CancellationToken ct) => await ReconcileAsync(invoiceId, ct);
    private async Task ReconcileAsync(Guid? invoiceId, CancellationToken ct)
    {
        var today = Today;
        // Recover committed invoices/payments after crashes. Delivery is deduplicated independently.
        var invoices = await db.Invoices.AsNoTracking().Include(x => x.Items).Where(x => x.Status == InvoiceStatus.ISSUED && (invoiceId == null || x.Id == invoiceId)).ToArrayAsync(ct);
        var paid = await totals.TotalsAsync(invoices.Select(x => x.Id).ToArray(), ct);
        var recipients = await db.InvoiceRecipients.AsNoTracking().ToArrayAsync(ct);
        foreach (var invoice in invoices) {
            var amount = paid.GetValueOrDefault(invoice.Id);
            foreach (var recipient in recipients.Where(x => x.InvoiceId == invoice.Id)) {
                var rental = recipient.Capacity == "RENTAL" ? invoice.Items.Where(x => x.FeeCodeSnapshot == RentalPricing.FeeCode).Sum(x => x.LineAmount) : 0;
                var service = invoice.Items.Where(x => x.FeeCodeSnapshot != RentalPricing.FeeCode).Sum(x => x.LineAmount);
                var rentalDetails = recipient.Capacity == "RENTAL" && invoice.Items.Any(x => x.FeeCodeSnapshot == RentalPricing.FeeCode)
                    ? " Tiền thuê: " + FinanceDocuments.Money(rental) + ". " + string.Join("; ", invoice.Items.Where(x => x.FeeCodeSnapshot == RentalPricing.FeeCode).Select(x => x.Description)) : "";
                await PublishAsync(invoice, recipient, $"invoice:{invoice.Id}", invoice.Type == InvoiceType.MONTHLY_RENTAL ? "Hóa đơn tiền thuê tháng mới" : "Hóa đơn phí dịch vụ mới",
                    $"Căn hộ {recipient.UnitNumber}, hóa đơn {invoice.InvoiceNumber}, kỳ {invoice.BillingPeriodStart:MM/yyyy}. Phí dịch vụ: {FinanceDocuments.Money(service)}.{rentalDetails} Tổng: {FinanceDocuments.Money(service + rental)}. Hạn {invoice.DueDate:dd/MM/yyyy}.", FinanceDocuments.Invoice(invoice, recipient, amount), ct);
                if (invoice.DueDate is { } due && today > due && amount < invoice.TotalAmount) {
                    await RemindOneAsync(invoice, recipient, amount, $"overdue:{invoice.Id}:day:{today:yyyyMMdd}", ct);
                }
            }
        }
        var events = await payments.ListAsync(invoices.Select(x => x.Id).ToArray(), ct);
        foreach (var p in events.Where(x => x.Status is "PENDING" or "CONFIRMED" or "REJECTED")) {
            var invoice = invoices.Single(x => x.Id == p.InvoiceId);
            foreach (var r in recipients.Where(x => x.InvoiceId == p.InvoiceId)) {
                var title = p.Status switch { "CONFIRMED" => "Thanh toán hóa đơn thành công", "PENDING" => "Thanh toán đang chờ xác minh", _ => "Khoản thanh toán chưa được chấp nhận" };
                var remaining = Math.Max(0, invoice.TotalAmount - paid.GetValueOrDefault(invoice.Id));
                var message = p.Status switch { "CONFIRMED" => $"Bạn đã thanh toán thành công {FinanceDocuments.Money(p.Amount)} cho hóa đơn {invoice.InvoiceNumber}, căn hộ {r.UnitNumber}. Phiếu {p.Number}. " + (remaining == 0 ? "Hóa đơn đã thanh toán đầy đủ, không còn dư nợ." : $"Dư nợ còn lại: {FinanceDocuments.Money(remaining)}."), "PENDING" => $"Khoản thanh toán {p.Number} cho hóa đơn {invoice.InvoiceNumber} đang chờ kế toán xác minh.", _ => $"Khoản thanh toán {p.Number} bị từ chối: {p.RejectionReason}" };
                var html = p.Status == "CONFIRMED" ? FinanceDocuments.Receipt(invoice, r, p, paid.GetValueOrDefault(invoice.Id)) : FinanceDocuments.Wrap(title, r, $"<p>{FinanceDocuments.Encode(message)}</p>");
                var email = p.Status == "CONFIRMED" ? await directory.RecipientEmailAsync(r.UserId, r.Email, ct) : null;
                await notices.PublishAsync(new(FinanceDocuments.EventId(RecipientEventKey($"payment:{p.Id}:{p.Status}", r)), r.UserId, email, title, message, html, "PAYMENT", p.Id, $"/resident/billing?invoice={invoice.Id}", p.Status == "CONFIRMED" ? p.Number + ".html" : null, PreviousRecipientEventId($"payment:{p.Id}:{p.Status}", r)), ct);
            }
        }
    }
    private async Task CaptureExistingRecipientsAsync(CancellationToken ct, Guid? onlyInvoiceId = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241001)", ct);
        // Existing issued demo invoices also need documents. Resolve the household at issue date,
        // never guess a recipient from the apartment's current owner or all residents.
        var existing = await db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.ISSUED
            && (onlyInvoiceId == null || x.Id == onlyInvoiceId) && !db.InvoiceRecipients.Any(r => r.InvoiceId == x.Id)).ToArrayAsync(ct);
        foreach (var group in existing.GroupBy(x => x.IssueDate ?? x.BillingPeriodStart)) {
            var heads = await directory.ListAsync(group.Key, ct);
            foreach (var i in group) {
                var matches = heads.Where(x => x.ApartmentId == i.ApartmentUnitId).ToArray();
                foreach (var h in matches) db.InvoiceRecipients.Add(new(i.Id, h.UserId, h.FullName, h.Email, h.UnitNumber, h.AreaM2, h.ResidentId, h.Capacity));
            }
        }
        // Attach only the very same addressed person after account onboarding, never a new apartment owner/head.
        var unlinked = await db.InvoiceRecipients.Where(r => r.UserId == null && r.ResidentId != null && (onlyInvoiceId == null || r.InvoiceId == onlyInvoiceId)).ToArrayAsync(ct);
        var accounts = await directory.LinkedAccountsAsync(unlinked.Select(r => r.ResidentId!.Value).Distinct().ToArray(), ct);
        foreach (var recipient in unlinked)
            if (accounts.TryGetValue(recipient.ResidentId!.Value, out var userId)) recipient.LinkAccount(userId);
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    private async Task GenerateAsync(DateOnly month, CancellationToken ct)
    {
        var households = await directory.ListChargeableAsync(Today, ct);
        var periodEnd = month.AddMonths(1).AddDays(-1);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241001)", ct);
        var serviceRate = await db.FeeRateRules.Include(x => x.FeeType).Where(x => x.FeeType!.Code == ServiceFeeCode && x.IsActive
            && x.EffectiveFrom <= month && (x.EffectiveTo == null || x.EffectiveTo >= month)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        var waterRate = await db.FeeRateRules.Include(x => x.FeeType).Where(x => x.FeeType!.Code == WaterFeeCode && x.IsActive
            && x.EffectiveFrom <= month && (x.EffectiveTo == null || x.EffectiveTo >= month)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        var waterVolumeRate = await db.FeeRateRules.Include(x => x.FeeType).Where(x => x.FeeType!.Code == WaterVolumeFeeCode && x.IsActive
            && x.EffectiveFrom <= month && (x.EffectiveTo == null || x.EffectiveTo >= month)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        var residentCounts = serviceRate is not null && waterRate is not null && waterVolumeRate is null
            ? await directory.ResidentCountsAsync(households.Select(x => x.ApartmentId).Distinct().ToArray(), month, ct)
            : new Dictionary<Guid, int>();
        var electricityRate = await db.FeeRateRules.Include(x => x.FeeType).Where(x => x.FeeType!.Code == ElectricityFeeCode && x.IsActive
            && x.EffectiveFrom <= month && (x.EffectiveTo == null || x.EffectiveTo >= month)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        var usageMonth = month.AddMonths(-1);
        var electricityUsages = electricityRate is null && waterVolumeRate is null ? [] : await db.ElectricityUsages.AsNoTracking()
            .Where(x => x.UsageMonth == usageMonth).ToArrayAsync(ct);
        var rentalRates = await db.FeeRateRules.Include(x => x.FeeType).Where(x => x.FeeType!.Code == RentalPricing.FeeCode
            && x.IsActive && x.BillingFrequencyCode == "MONTHLY" && x.EffectiveFrom <= periodEnd
            && (x.EffectiveTo == null || x.EffectiveTo >= month)).ToArrayAsync(ct);
        var existing = await db.Invoices.AsNoTracking().Include(x => x.Items).Where(x => x.BillingPeriodStart == month && x.BillingPeriodEnd == periodEnd).ToArrayAsync(ct);
        var deferredServiceBills = 0;
        foreach (var group in households.GroupBy(x => x.ApartmentId)) {
            if (group.Count() > 1 && group.Any(x => x.Capacity != "OWNER")) continue;
            var h = group.First();
            bool Exists(InvoiceType type) => existing.Any(i => i.ApartmentUnitId == h.ApartmentId &&
                (i.Type == type || i.Type == null && i.Items.Any(line => (line.FeeCodeSnapshot == RentalPricing.FeeCode) == (type == InvoiceType.MONTHLY_RENTAL))));
            void Add(Invoice invoice, Guid actor, string note) {
                invoice.Issue(actor, month, DateTimeOffset.UtcNow, month.AddDays(9));
                db.Invoices.Add(invoice);
                db.InvoiceStatusHistory.Add(new(invoice.Id, InvoiceStatus.ISSUED, actor, DateTimeOffset.UtcNow, InvoiceStatus.DRAFT, note));
                foreach (var recipient in group) db.InvoiceRecipients.Add(new(invoice.Id, recipient.UserId, recipient.FullName, recipient.Email,
                    recipient.UnitNumber, recipient.AreaM2, recipient.ResidentId, recipient.Capacity));
            }
            var electricityUsage = electricityUsages.SingleOrDefault(x => x.ApartmentId == h.ApartmentId);
            if (!Exists(InvoiceType.SERVICE_FEE) && (serviceRate is null || electricityRate is not null && electricityUsage?.Kwh is null || waterVolumeRate is not null && electricityUsage?.WaterM3 is null)) deferredServiceBills++;
            if (serviceRate is not null && !Exists(InvoiceType.SERVICE_FEE) && (electricityRate is null || electricityUsage?.Kwh is not null) && (waterVolumeRate is null || electricityUsage?.WaterM3 is not null)) {
                if(h.AreaM2 <= 0) throw new InvalidOperationException("Diện tích căn hộ không hợp lệ; không thể phát hành hóa đơn dịch vụ.");
                var service = new Invoice($"HD-{month:yyyyMM}-{h.UnitNumber}", h.ApartmentId, month, periodEnd, serviceRate.CreatedBy, DateTimeOffset.UtcNow, invoiceType: InvoiceType.SERVICE_FEE);
                service.AddItem(serviceRate.FeeType!.Code, serviceRate.FeeType.Name, "AREA", h.AreaM2, serviceRate.UnitRate,
                    serviceRate.CreatedBy, DateTimeOffset.UtcNow, serviceRate.FeeTypeId, serviceRate.Id,
                    description: "Đã gồm phí giữ xe và các dịch vụ chung;", unitName: "m²");
                if (waterVolumeRate is null && waterRate is not null && residentCounts.GetValueOrDefault(h.ApartmentId) is var people && people > 0)
                    service.AddItem(waterRate.FeeType!.Code, waterRate.FeeType.Name, "PERSON", people, waterRate.UnitRate,
                        serviceRate.CreatedBy, DateTimeOffset.UtcNow, waterRate.FeeTypeId, waterRate.Id,
                        description: $"Chốt {people} người đang cư trú ngày {month:dd/MM/yyyy}; thu nguyên tháng.", unitName: "người");
                if (electricityRate is not null && electricityUsage is { Kwh: > 0 })
                    service.AddItem(electricityRate.FeeType!.Code, electricityRate.FeeType.Name, "KWH", electricityUsage.Kwh!.Value, electricityRate.UnitRate,
                        serviceRate.CreatedBy, DateTimeOffset.UtcNow, electricityRate.FeeTypeId, electricityRate.Id,
                        description: $"Điện sử dụng tháng {usageMonth:MM/yyyy}; số kWh do manager nhập và chốt trước khi phát hành.", unitName: "kWh");
                if (waterVolumeRate is not null && electricityUsage?.WaterM3 is > 0 && electricityUsage.WaterM3 is { } waterQuantity)
                    service.AddItem(waterVolumeRate.FeeType!.Code, waterVolumeRate.FeeType.Name, "VOLUME", waterQuantity, waterVolumeRate.UnitRate,
                        serviceRate.CreatedBy, DateTimeOffset.UtcNow, waterVolumeRate.FeeTypeId, waterVolumeRate.Id,
                        description: $"Nước sử dụng tháng {usageMonth:MM/yyyy}; sản lượng m³ do manager nhập và chốt trước khi phát hành.", unitName: "m³");
                Add(service,serviceRate.CreatedBy,"Tự động phát hành phí dịch vụ tháng");
            }
            if(h.Capacity != "RENTAL" || h.RentalStart is not { } start || Exists(InvoiceType.MONTHLY_RENTAL)) continue;
            var first = start > month ? start : month;
            var last = h.RentalEnd is { } end && end < periodEnd ? end : periodEnd;
            var rules = rentalRates.Where(x => (x.ApartmentUnitId == h.ApartmentId || x.ApartmentTypeId == h.ApartmentTypeId && h.ApartmentTypeId != null) && x.EffectiveFrom <= last
                && (x.EffectiveTo == null || x.EffectiveTo >= first)).OrderBy(x => x.EffectiveFrom).ToArray();
            if (rules.Any(x => x.ApartmentUnitId == h.ApartmentId)) rules = rules.Where(x => x.ApartmentUnitId == h.ApartmentId).ToArray();
            if(rules.Length == 0) continue;
            if(rules[0].EffectiveFrom > first) first=rules[0].EffectiveFrom;
            var covered=first;
            foreach(var rule in rules) { if(rule.EffectiveFrom > covered) break; covered=(rule.EffectiveTo is { } to && to < last ? to : last).AddDays(1); }
            if(covered <= last) continue;
            var rental = new Invoice($"HT-{month:yyyyMM}-{h.UnitNumber}",h.ApartmentId,month,periodEnd,rules[0].CreatedBy,DateTimeOffset.UtcNow,invoiceType:InvoiceType.MONTHLY_RENTAL);
            foreach(var rule in rules) RentalInvoiceCalculation.Append(rental,rule,h.Capacity,start,h.RentalEnd,rules[0].CreatedBy,DateTimeOffset.UtcNow,h.ApartmentTypeId);
            if(rental.Items.Count > 0) Add(rental,rules[0].CreatedBy,"Tự động phát hành tiền thuê tháng");
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (deferredServiceBills > 0) logger?.LogInformation("Billing period {Period}: {Count} service invoices await pricing or utility readings; no estimated consumption was charged.", month, deferredServiceBills);
    }
    public async Task<IReadOnlyList<ResidentInvoice>> InvoicesAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is null) {
            var invoices = await db.Invoices.AsNoTracking().Include(x => x.Items).Where(x => x.Status == InvoiceStatus.ISSUED).OrderByDescending(x => x.BillingPeriodStart).ToArrayAsync(ct);
            var amounts = await totals.TotalsAsync(invoices.Select(x => x.Id).ToArray(), ct);
            var result = new List<ResidentInvoice>();
            foreach (var i in invoices) {
                var recipient = await DocumentRecipientAsync(i, null, ct);
                result.Add(new(i.Id, i.InvoiceNumber, recipient?.UnitNumber ?? "Chưa xác định căn hộ", recipient?.FullName ?? "", i.BillingPeriodStart,
                    i.DueDate, i.TotalAmount, amounts.GetValueOrDefault(i.Id), Math.Max(0, i.TotalAmount - amounts.GetValueOrDefault(i.Id)),
                    i.Items.Where(x => x.FeeCodeSnapshot != RentalPricing.FeeCode).Sum(x => x.LineAmount), i.Items.Where(x => x.FeeCodeSnapshot == RentalPricing.FeeCode).Sum(x => x.LineAmount), recipient?.Capacity ?? "HOUSEHOLD_HEAD", i.Type?.ToString()));
            }
            return result;
        }
        var q = from i in db.Invoices.AsNoTracking().Include(x => x.Items) join r in db.InvoiceRecipients.AsNoTracking() on i.Id equals r.InvoiceId
                where i.Status == InvoiceStatus.ISSUED && (userId == null || r.UserId == userId)
                orderby i.BillingPeriodStart descending select new { i, r };
        var rows = await q.ToArrayAsync(ct); var paid = await totals.TotalsAsync(rows.Select(x => x.i.Id).ToArray(), ct);
        return rows.Select(x => new ResidentInvoice(x.i.Id, x.i.InvoiceNumber, x.r.UnitNumber, x.r.FullName, x.i.BillingPeriodStart,
            x.i.DueDate, x.i.TotalAmount, paid.GetValueOrDefault(x.i.Id), Math.Max(0, x.i.TotalAmount - paid.GetValueOrDefault(x.i.Id)),
            x.i.Items.Where(i => i.FeeCodeSnapshot != RentalPricing.FeeCode).Sum(i => i.LineAmount), x.i.Items.Where(i => i.FeeCodeSnapshot == RentalPricing.FeeCode).Sum(i => i.LineAmount), x.r.Capacity, x.i.Type?.ToString())).ToArray();
    }
    public async Task<FinanceDocument?> InvoiceDocumentAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id && x.Status == InvoiceStatus.ISSUED, ct);
        if (invoice is null) return null;
        var r = await DocumentRecipientAsync(invoice, userId, ct);
        if (r is null) return null;
        return invoice is null ? null : Document(FinanceDocuments.Invoice(invoice, r, await totals.TotalAsync(id, ct)), invoice.InvoiceNumber);
    }
    public async Task<FinanceDocument?> ReceiptAsync(Guid paymentId, Guid? userId, CancellationToken ct)
    {
        var p = await payments.FindAsync(paymentId, ct); if (p?.Status != "CONFIRMED") return null;
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == p.InvoiceId, ct);
        var r = await DocumentRecipientAsync(invoice, userId, ct);
        if (r is null) return null;
        return Document(FinanceDocuments.Receipt(invoice, r, p, await totals.TotalAsync(p.InvoiceId, ct)), p.Number);
    }
    public async Task<int> RemindAsync(IReadOnlyList<Guid> ids, CancellationToken ct, bool overdueOnly = false, Guid? requestId = null)
    {
        await ConfigureTimeZoneAsync(ct);
        var invoices = await db.Invoices.AsNoTracking().Include(x => x.Items).Where(x => ids.Contains(x.Id) && x.Status == InvoiceStatus.ISSUED).ToArrayAsync(ct);
        var paid = await totals.TotalsAsync(ids, ct); var count = 0;
        foreach (var i in invoices.Where(x => paid.GetValueOrDefault(x.Id) < x.TotalAmount && (!overdueOnly || x.DueDate < Today))) {
            await CaptureExistingRecipientsAsync(ct, i.Id);
            var recipients = await db.InvoiceRecipients.AsNoTracking().Where(x => x.InvoiceId == i.Id).ToArrayAsync(ct);
            var key = requestId.HasValue ? $"manual:{i.Id}:request:{requestId.Value:N}" : $"manual:{i.Id}:{Today:yyyyMMdd}";
            foreach (var r in recipients) { await RemindOneAsync(i, r, paid.GetValueOrDefault(i.Id), key, ct); count++; }
        }
        return count;
    }
    public async Task<int> SendInvoiceAsync(Guid id, CancellationToken ct, Guid? requestId = null)
    {
        await ConfigureTimeZoneAsync(ct);
        var invoice = await db.Invoices.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy hóa đơn.");
        if (invoice.Status != InvoiceStatus.ISSUED) throw new InvalidOperationException("Chỉ được gửi hóa đơn đã phát hành.");
        await CaptureExistingRecipientsAsync(ct, id);
        var recipients = await db.InvoiceRecipients.AsNoTracking().Where(x => x.InvoiceId == id).ToArrayAsync(ct);
        if (recipients.Length == 0 || recipients.Any(x => x.UserId == null && string.IsNullOrWhiteSpace(x.Email)))
            throw new InvalidOperationException("Hóa đơn chưa có người nhận hợp lệ. Kiểm tra hồ sơ chủ hộ tại kỳ phát hành.");
        var paid = await totals.TotalAsync(id, ct);
        var sendKey = requestId.HasValue ? $"send-invoice:{id}:request:{requestId.Value:N}" : $"send-invoice:{id}:{invoice.UpdatedAt.UtcTicks}:{Today:yyyyMMdd}";
        foreach (var recipient in recipients)
            await PublishAsync(invoice, recipient, sendKey, invoice.Note?.StartsWith("[ĐIỀU CHỈNH]") == true ? "Hóa đơn đã điều chỉnh" : "Hóa đơn đã phát hành",
                $"Hóa đơn {invoice.InvoiceNumber}, căn hộ {recipient.UnitNumber}, kỳ {invoice.BillingPeriodStart:MM/yyyy}. Tổng {FinanceDocuments.Money(invoice.TotalAmount)}; còn phải thu {FinanceDocuments.Money(Math.Max(0, invoice.TotalAmount - paid))}. Hạn {invoice.DueDate:dd/MM/yyyy}.",
                FinanceDocuments.Invoice(invoice, recipient, paid), ct);
        return recipients.Length;
    }
    private Task RemindOneAsync(Invoice i, InvoiceRecipient r, decimal paid, string key, CancellationToken ct)
    {
        var overdue = i.DueDate is { } due && Today > due;
        var title = overdue ? "Thông báo công nợ quá hạn" : "Thông báo nhắc thanh toán hóa đơn";
        var message = $"Căn hộ {r.UnitNumber}, hóa đơn {i.InvoiceNumber}, kỳ {i.BillingPeriodStart:MM/yyyy}: còn nợ {FinanceDocuments.Money(Math.Max(0, i.TotalAmount - paid))}. Hạn thanh toán: {i.DueDate:dd/MM/yyyy}. "
            + (overdue ? $"Đã quá hạn {Today.DayNumber - i.DueDate!.Value.DayNumber} ngày. Vui lòng thanh toán khoản còn nợ." : "Vui lòng thanh toán đúng hạn.");
        return PublishAsync(i, r, key, title, message, FinanceDocuments.Invoice(i, r, paid, title, message), ct);
    }
    private async Task PublishAsync(Invoice i, InvoiceRecipient r, string key, string title, string message, string html, CancellationToken ct) =>
        await notices.PublishAsync(new(FinanceDocuments.EventId(RecipientEventKey(key, r)), r.UserId, await directory.RecipientEmailAsync(r.UserId, r.Email, ct), title, message, html, "INVOICE", i.Id, $"/resident/billing?invoice={i.Id}", PreviousRecipientEventId: PreviousRecipientEventId(key, r)), ct);
    private static Guid? PreviousRecipientEventId(string key, InvoiceRecipient r) => r.UserId is not null && r.ResidentId is not null
        ? FinanceDocuments.EventId(key + ":resident:" + r.ResidentId) : null;
    private static string RecipientEventKey(string key, InvoiceRecipient r) => r.UserId.HasValue ? key : key + ":resident:" + (r.ResidentId ?? r.Id);
    private static FinanceDocument Document(string html, string number) => new(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", number + ".html");
    private async Task<InvoiceRecipient?> DocumentRecipientAsync(Invoice invoice, Guid? userId, CancellationToken ct)
    {
        var snapshot = await db.InvoiceRecipients.AsNoTracking().FirstOrDefaultAsync(x => x.InvoiceId == invoice.Id && (userId == null || x.UserId == userId), ct);
        if (snapshot is not null || userId is not null) return snapshot;
        var household = await directory.DescribeAsync(invoice.ApartmentUnitId, invoice.IssueDate ?? invoice.BillingPeriodStart, ct);
        return household is null ? null : new(invoice.Id, household.UserId, household.FullName, household.Email, household.UnitNumber, household.AreaM2);
    }
}

