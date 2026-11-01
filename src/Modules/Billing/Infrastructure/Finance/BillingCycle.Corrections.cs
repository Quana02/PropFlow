using System.Security.Cryptography;
using PropFlow.Modules.Billing.Application.Finance;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Domain.ElectricityUsages;
using PropFlow.Modules.Billing.Domain.InvoiceItems;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;

public sealed partial class BillingCycle
{
    // Explicit operator use case, not part of automatic generation or payment/reminder endpoints.
    // The original document is archived in the existing history; its identity and payment records stay intact.
    public async Task CorrectUnpaidServiceInvoiceAsync(Guid id, ServiceInvoiceCorrection input, Guid actor, CancellationToken ct)
    {
        if (actor == Guid.Empty || !Regex.IsMatch(input.Key ?? "", @"^[A-Za-z0-9:_-]{8,100}$") || string.IsNullOrWhiteSpace(input.Reason))
            throw new ArgumentException("Cần người thực hiện, mã điều chỉnh và lý do rõ ràng.");
        foreach (var rate in new[] { input.ServiceUnitRate, input.WaterUnitRate, input.ElectricityUnitRate })
            if (rate <= 0 || rate > 1000000 || rate != decimal.Round(rate, 0)) throw new ArgumentException("Đơn giá phải là số VND nguyên từ 1 đến 1.000.000.");
        foreach (var quantity in new[] { input.WaterM3, input.Kwh })
            if (quantity < 0 || quantity > 1000000 || quantity != decimal.Round(quantity, 3)) throw new ArgumentException("Sản lượng không hợp lệ.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241001)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);
        var marker = "\"CorrectionKey\":\"" + input.Key + "\"";
        var prior = await db.InvoiceStatusHistory.AsNoTracking().FirstOrDefaultAsync(x => x.InvoiceId == id && x.Reason != null && x.Reason.Contains(marker), ct);
        if (prior is not null)
        {
            if (JsonDocument.Parse(prior.Reason!).RootElement.GetProperty("PayloadHash").GetString() != hash)
                throw new InvalidOperationException("Mã điều chỉnh đã được dùng với dữ liệu khác.");
            return;
        }
        var invoice = await db.Invoices.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy hóa đơn cần điều chỉnh.");
        if (invoice.Status != InvoiceStatus.ISSUED || invoice.Type == InvoiceType.MONTHLY_RENTAL || invoice.Items.Any(x => x.FeeCodeSnapshot == RentalPricing.FeeCode))
            throw new InvalidOperationException("Chỉ điều chỉnh hóa đơn dịch vụ đã phát hành, không gộp tiền thuê.");
        if ((await payments.ListAsync([id], ct)).Count > 0 || await totals.TotalAsync(id, ct) != 0)
            throw new InvalidOperationException("Hóa đơn đã có giao dịch thanh toán; cần quy trình đối soát trước khi điều chỉnh.");
        var recipients = await db.InvoiceRecipients.AsNoTracking().Where(x => x.InvoiceId == id).ToArrayAsync(ct);
        var areas = recipients.Select(x => x.AreaM2).Distinct().ToArray();
        if (areas.Length != 1 || areas[0] <= 0) throw new InvalidOperationException("Thiếu snapshot diện tích/người nhận để điều chỉnh đúng hóa đơn.");
        var area = areas[0]; var now = DateTimeOffset.UtcNow; var usageMonth = invoice.BillingPeriodStart.AddMonths(-1);
        var oldItems = invoice.Items.ToArray();
        var oldReading = await db.ElectricityUsages.SingleOrDefaultAsync(x => x.ApartmentId == invoice.ApartmentUnitId && x.UsageMonth == usageMonth, ct);
        var before = new { invoice.InvoiceNumber, invoice.TotalAmount, invoice.Note, invoice.Status, invoice.IssueDate, invoice.DueDate,
            Items = oldItems.Select(x => new { x.Id, x.FeeTypeId, x.FeeRateRuleId, x.FeeCodeSnapshot, x.FeeNameSnapshot, x.CalculationMethodCodeSnapshot,
                x.Quantity, x.UnitName, x.UnitRate, x.LineAmount, x.Description, x.AmountDivisor, x.CreatedAt }).ToArray(),
            Reading = oldReading is null ? null : new { oldReading.Kwh, oldReading.WaterM3, oldReading.UsageMonth, oldReading.UpdatedBy, oldReading.UpdatedAt } };
        var replacement = new List<InvoiceItem> { new(id, ServiceFeeCode, "Phí dịch vụ chung cư", "AREA", area, input.ServiceUnitRate, now,
            description: "Đã gồm phí giữ xe và các dịch vụ chung;", unitName: "m²") };
        if (input.WaterM3 > 0) replacement.Add(new(id, WaterVolumeFeeCode, "Nước sinh hoạt", "VOLUME", input.WaterM3, input.WaterUnitRate, now,
            description: $"Sản lượng nước tháng {usageMonth:MM/yyyy}", unitName: "m³"));
        if (input.Kwh > 0) replacement.Add(new(id, ElectricityFeeCode, "Tiền điện", "KWH", input.Kwh, input.ElectricityUnitRate, now,
            description: $"Điện tháng {usageMonth:MM/yyyy}", unitName: "kWh"));
        var summary = $"{input.Reason} Tổng trước: {FinanceDocuments.Money(invoice.TotalAmount)}; tổng sau: {FinanceDocuments.Money(replacement.Sum(x => x.LineAmount))}. Phí dịch vụ đã gồm giữ xe; nước {input.WaterM3} m³; điện {input.Kwh} kWh.";
        invoice.CorrectIssuedItems(replacement, actor, summary, now);
        db.InvoiceItems.AddRange(replacement);
        db.InvoiceItems.RemoveRange(oldItems);
        if (oldReading is null) db.ElectricityUsages.Add(new ElectricityUsage(invoice.ApartmentUnitId, usageMonth, input.Kwh, input.WaterM3, actor, now));
        else oldReading.UpdateUtilities(input.Kwh, input.WaterM3, actor, now);
        db.InvoiceStatusHistory.Add(new InvoiceStatusHistory(id, InvoiceStatus.ISSUED, actor, now, InvoiceStatus.ISSUED,
            JsonSerializer.Serialize(new { AuditKind = "SERVICE_INVOICE_CORRECTION", CorrectionKey = input.Key, PayloadHash = hash, Summary = summary, Actor = actor, At = now,
                Before = before, After = new { invoice.TotalAmount, UsageMonth = usageMonth, input, Items = replacement.Select(x => new { x.FeeCodeSnapshot, x.FeeNameSnapshot, x.Quantity, x.UnitName, x.UnitRate, x.LineAmount }).ToArray() } })));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
