using System.Text;
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
public sealed class BillingCycle(BillingDbContext db, IBillingHouseholdDirectory directory,
    IConfirmedPaymentSource totals, IPaymentNoticeSource payments, IFinanceNoticePublisher notices, TimeProvider? clock = null) : IBillingCycle
{
    public const string ServiceFeeCode = "SERVICE_AREA_MONTHLY";
    private DateOnly Today => DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
    public async Task<IReadOnlyList<ServiceRateView>> RatesAsync(CancellationToken ct) => await db.FeeRateRules.AsNoTracking()
        .Where(x => x.FeeType!.Code == ServiceFeeCode && x.IsActive).OrderByDescending(x => x.EffectiveFrom)
        .Select(x => new ServiceRateView(x.Id, x.UnitRate, x.EffectiveFrom, x.CreatedBy)).ToArrayAsync(ct);
    public async Task SaveRateAsync(ServiceRateInput input, Guid actor, CancellationToken ct)
    {
        if (input.UnitRate <= 0 || input.UnitRate > 1000000 || input.EffectiveFrom.Day != 1)
            throw new ArgumentException("Đơn giá phải từ 1 đến 1.000.000 VND/m²; ngày hiệu lực phải là đầu tháng.");
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
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task RunAsync(CancellationToken ct)
    {
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
                    var week = (today.DayNumber - due.DayNumber - 1) / 7;
                    await RemindOneAsync(invoice, recipient, amount, $"overdue:{invoice.Id}:{week}", ct);
                }
            }
        }
        var events = await payments.ListAsync(invoices.Select(x => x.Id).ToArray(), ct);
        foreach (var p in events.Where(x => x.Status is "PENDING" or "CONFIRMED" or "REJECTED")) {
            var invoice = invoices.Single(x => x.Id == p.InvoiceId);
            foreach (var r in recipients.Where(x => x.InvoiceId == p.InvoiceId)) {
                var title = p.Status switch { "CONFIRMED" => "Xác nhận thanh toán hóa đơn", "PENDING" => "Thanh toán đang chờ xác minh", _ => "Khoản thanh toán chưa được chấp nhận" };
                var message = p.Status switch { "CONFIRMED" => $"Đã nhận {FinanceDocuments.Money(p.Amount)} cho hóa đơn {invoice.InvoiceNumber}. Phiếu {p.Number}.", "PENDING" => $"Khoản thanh toán {p.Number} cho hóa đơn {invoice.InvoiceNumber} đang chờ kế toán xác minh.", _ => $"Khoản thanh toán {p.Number} bị từ chối: {p.RejectionReason}" };
                var html = p.Status == "CONFIRMED" ? FinanceDocuments.Receipt(invoice, r, p, paid.GetValueOrDefault(invoice.Id)) : FinanceDocuments.Wrap(title, r, $"<p>{FinanceDocuments.Encode(message)}</p>");
                var email = p.Status == "CONFIRMED" ? await directory.RecipientEmailAsync(r.UserId, r.Email, ct) : null;
                await notices.PublishAsync(new(FinanceDocuments.EventId(RecipientEventKey($"payment:{p.Id}:{p.Status}", r)), r.UserId, email, title, message, html, "PAYMENT", p.Id, $"/resident/billing?invoice={invoice.Id}", p.Status == "CONFIRMED" ? p.Number + ".html" : null, PreviousRecipientEventId($"payment:{p.Id}:{p.Status}", r)), ct);
            }
        }
    }
    private async Task CaptureExistingRecipientsAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241001)", ct);
        // Existing issued demo invoices also need documents. Resolve the household at issue date,
        // never guess a recipient from the apartment's current owner or all residents.
        var existing = await db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.ISSUED
            && !db.InvoiceRecipients.Any(r => r.InvoiceId == x.Id)).ToArrayAsync(ct);
        foreach (var group in existing.GroupBy(x => x.IssueDate ?? x.BillingPeriodStart)) {
            var heads = await directory.ListAsync(group.Key, ct);
            foreach (var i in group) {
                var matches = heads.Where(x => x.ApartmentId == i.ApartmentUnitId).ToArray();
                foreach (var h in matches) db.InvoiceRecipients.Add(new(i.Id, h.UserId, h.FullName, h.Email, h.UnitNumber, h.AreaM2, h.ResidentId, h.Capacity));
            }
        }
        // Attach only the very same addressed person after account onboarding, never a new apartment owner/head.
        var unlinked = await db.InvoiceRecipients.Where(r => r.UserId == null && r.ResidentId != null).ToArrayAsync(ct);
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
        var rentalRates = await db.FeeRateRules.Include(x => x.FeeType).Where(x => x.FeeType!.Code == RentalPricing.FeeCode
            && x.IsActive && x.BillingFrequencyCode == "MONTHLY" && x.EffectiveFrom <= periodEnd
            && (x.EffectiveTo == null || x.EffectiveTo >= month)).ToArrayAsync(ct);
        var existing = await db.Invoices.AsNoTracking().Include(x => x.Items).Where(x => x.BillingPeriodStart == month && x.BillingPeriodEnd == periodEnd).ToArrayAsync(ct);
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
            if (serviceRate is not null && !Exists(InvoiceType.SERVICE_FEE)) {
                if(h.AreaM2 <= 0) throw new InvalidOperationException("Diện tích căn hộ không hợp lệ; không thể phát hành hóa đơn dịch vụ.");
                var service = new Invoice($"HD-{month:yyyyMM}-{h.UnitNumber}", h.ApartmentId, month, periodEnd, serviceRate.CreatedBy, DateTimeOffset.UtcNow, invoiceType: InvoiceType.SERVICE_FEE);
                service.AddItem(serviceRate.FeeType!.Code, serviceRate.FeeType.Name, "AREA", h.AreaM2, serviceRate.UnitRate,
                    serviceRate.CreatedBy, DateTimeOffset.UtcNow, serviceRate.FeeTypeId, serviceRate.Id, unitName: "m²");
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
    public async Task<int> RemindAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var invoices = await db.Invoices.AsNoTracking().Include(x => x.Items).Where(x => ids.Contains(x.Id) && x.Status == InvoiceStatus.ISSUED).ToArrayAsync(ct);
        var paid = await totals.TotalsAsync(ids, ct); var count = 0;
        foreach (var i in invoices.Where(x => paid.GetValueOrDefault(x.Id) < x.TotalAmount)) {
            var recipients = await db.InvoiceRecipients.AsNoTracking().Where(x => x.InvoiceId == i.Id).ToArrayAsync(ct);
            foreach (var r in recipients) { await RemindOneAsync(i, r, paid.GetValueOrDefault(i.Id), $"manual:{i.Id}:{Today:yyyyMMdd}", ct); count++; }
        }
        return count;
    }
    private Task RemindOneAsync(Invoice i, InvoiceRecipient r, decimal paid, string key, CancellationToken ct) =>
        PublishAsync(i, r, key, i.Type == InvoiceType.MONTHLY_RENTAL ? "Nhắc thanh toán tiền thuê tháng" : "Nhắc thanh toán phí dịch vụ", $"Hóa đơn {i.InvoiceNumber} còn {FinanceDocuments.Money(i.TotalAmount - paid)}. Hạn {i.DueDate:dd/MM/yyyy}.", FinanceDocuments.Invoice(i, r, paid), ct);
    private Task PublishAsync(Invoice i, InvoiceRecipient r, string key, string title, string message, string html, CancellationToken ct) =>
        notices.PublishAsync(new(FinanceDocuments.EventId(RecipientEventKey(key, r)), r.UserId, r.Email, title, message, html, "INVOICE", i.Id, $"/resident/billing?invoice={i.Id}", PreviousRecipientEventId: PreviousRecipientEventId(key, r)), ct);
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

