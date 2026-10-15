using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.Modules.Payments.Infrastructure.Finance;

public sealed class PaymentFinanceStore(PaymentsDbContext db, IInvoiceFinancialSource invoices) : IPaymentFinanceStore
{
    public async Task<MutationResult> SubmitAsync(CollectPaymentRequest request, string key, Guid actorId, CancellationToken ct)
    {
        if (!await invoices.IsRecipientAsync(request.InvoiceId, actorId, ct)) return new(MutationOutcome.NotFound);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{actorId}|{System.Text.Json.JsonSerializer.Serialize(request)}")));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 2))", ct);
        await LockInvoiceAsync(request.InvoiceId, ct);
        var prior = await db.PaymentOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct);
        if (prior is not null) return prior.PayloadHash == hash && prior.Action == "SUBMIT"
            ? new(MutationOutcome.Success, await FindAsync(prior.PaymentId, ct)) : new(MutationOutcome.IdempotencyConflict);
        var invoice = await invoices.FindAsync(request.InvoiceId, ct);
        if (invoice is null || invoice.Status != "ISSUED") return new(MutationOutcome.NotFound);
        if (await db.Payments.AnyAsync(x => x.InvoiceId == request.InvoiceId && x.Status == PaymentStatus.PENDING, ct)) return new(MutationOutcome.Conflict);
        var paid = await db.Payments.Where(x => x.InvoiceId == request.InvoiceId && x.Status == PaymentStatus.CONFIRMED).SumAsync(x => x.Amount, ct);
        if (request.Amount <= 0 || request.Amount != decimal.Round(request.Amount) || request.Amount > invoice.TotalAmount - paid) return new(MutationOutcome.Overpayment);
        var now = DateTimeOffset.UtcNow;
        var payment = new Payment($"PT-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..20]}", request.InvoiceId, request.Amount, "BANK_TRANSFER", now, now, actorId, request.ReferenceNumber, request.Note);
        db.Payments.Add(payment);
        db.PaymentStatusHistory.Add(new PaymentStatusHistory(payment.Id, PaymentStatus.PENDING, actorId, now, null, "Cư dân gửi xác nhận đã thanh toán, chờ kế toán kiểm tra"));
        db.PaymentOperations.Add(new PaymentOperation(key, payment.Id, "SUBMIT", hash, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(MutationOutcome.Success, await FindAsync(payment.Id, ct));
    }
    public async Task<IReadOnlyList<PaymentRow>> ByInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var rows = await db.Payments.AsNoTracking().Where(x => x.InvoiceId == invoiceId).OrderByDescending(x => x.PaymentDate).ThenBy(x => x.Id)
            .Select(x => new PaymentRow(x.Id, x.PaymentNumber, x.InvoiceId, x.Amount, x.PaymentMethodCode, x.PaymentDate,
                x.ReferenceNumber, x.Note, x.Status.ToString(), x.ConfirmedAt, null, null, null)).ToArrayAsync(ct);
        return await WithInvoiceIdentityAsync(rows, ct);
    }
    public async Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string sortDirection, CancellationToken ct, string? filter = null)
    {
        var parsed = Enum.Parse<PaymentStatus>(status, true); var q = db.Payments.AsNoTracking().Where(x => x.Status == parsed);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim(); var s = term.ToUpperInvariant();
            var matchedInvoices = (await invoices.ListIssuedAsync(ct)).Where(x => x.InvoiceNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.ResidentName?.Contains(term, StringComparison.OrdinalIgnoreCase) == true || x.ApartmentNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) == true).Select(x => x.Id).ToArray();
            q = q.Where(x => x.PaymentNumber.ToUpper().Contains(s) || x.ReferenceNumber != null && x.ReferenceNumber.ToUpper().Contains(s) || matchedInvoices.Contains(x.InvoiceId));
        }
        var counts = await q.GroupBy(x => x.PaymentMethodCode).Select(g => new { Method = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Method, x => x.Count, ct);
        counts["ALL"] = counts.Values.Sum();
        if (!string.IsNullOrWhiteSpace(filter) && filter != "ALL") q = q.Where(x => x.PaymentMethodCode == filter);
        var total = await q.CountAsync(ct);
        page = Math.Min(Math.Max(1, page), Math.Max(1, (int)Math.Ceiling(total / (double)pageSize))); q = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) ? q.OrderBy(x => x.PaymentDate).ThenBy(x => x.Id) : q.OrderByDescending(x => x.PaymentDate).ThenBy(x => x.Id);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new PaymentRow(x.Id, x.PaymentNumber, x.InvoiceId, x.Amount, x.PaymentMethodCode, x.PaymentDate, x.ReferenceNumber, x.Note, x.Status.ToString(), x.ConfirmedAt, null, null, null)).ToArrayAsync(ct);
        return new(total, page, pageSize, await WithInvoiceIdentityAsync(items, ct), counts);
    }
    public async Task<PaymentRow?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Payments.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new PaymentRow(x.Id, x.PaymentNumber, x.InvoiceId, x.Amount, x.PaymentMethodCode, x.PaymentDate, x.ReferenceNumber, x.Note, x.Status.ToString(), x.ConfirmedAt, null, null, null)).SingleOrDefaultAsync(ct);
        return row is null ? null : (await WithInvoiceIdentityAsync([row], ct))[0];
    }
    private async Task<PaymentRow[]> WithInvoiceIdentityAsync(IReadOnlyList<PaymentRow> rows, CancellationToken ct)
    {
        var identities = (await invoices.FindManyAsync(rows.Select(x => x.InvoiceId).Distinct().ToArray(), ct)).ToDictionary(x => x.Id);
        return rows.Select(row => identities.TryGetValue(row.InvoiceId, out var identity)
            ? row with { InvoiceNumber = identity.InvoiceNumber, ApartmentNumber = identity.ApartmentNumber, ResidentName = identity.ResidentName }
            : row).ToArray();
    }
    public async Task<MutationResult> ChangeAsync(Guid id, string action, string? reason, string key, Guid actorId, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{action}|{reason}")));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 2))", ct);
        // Serialize all confirmations for the same invoice, including distinct payment rows.
        var invoiceId = await db.Payments.Where(x => x.Id == id).Select(x => (Guid?)x.InvoiceId).SingleOrDefaultAsync(ct);
        if (invoiceId is null) return new(MutationOutcome.NotFound);
        await LockInvoiceAsync(invoiceId.Value, ct);
        var prior = await db.PaymentOperations.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (prior is not null) return prior.PayloadHash == hash ? new(MutationOutcome.Success, await FindAsync(id, ct)) : new(MutationOutcome.IdempotencyConflict);
        var payment = await db.Payments.FromSqlInterpolated($"SELECT * FROM payments.payments WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (payment is null) return new(MutationOutcome.NotFound); if (payment.Status != PaymentStatus.PENDING) return new(MutationOutcome.Conflict);
        var invoice = await invoices.FindAsync(payment.InvoiceId, ct); if (invoice is null || invoice.Status != "ISSUED") return new(MutationOutcome.Conflict);
        if (action == "CONFIRM") { var paid = await db.Payments.Where(x => x.InvoiceId == payment.InvoiceId && x.Status == PaymentStatus.CONFIRMED).SumAsync(x => x.Amount, ct); if (payment.Amount > invoice.TotalAmount - paid) return new(MutationOutcome.Overpayment); payment.Confirm(actorId, invoice.TotalAmount - paid, DateTimeOffset.UtcNow); }
        else payment.Reject(actorId, reason!, DateTimeOffset.UtcNow);
        db.PaymentStatusHistory.Add(new PaymentStatusHistory(payment.Id, payment.Status, actorId, DateTimeOffset.UtcNow, PaymentStatus.PENDING, reason)); db.PaymentOperations.Add(new PaymentOperation(key, id, action, hash, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(MutationOutcome.Success, await FindAsync(id, ct));
    }
    public async Task<MutationResult> CollectAsync(CollectPaymentRequest request, string key, Guid actorId, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(request))));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 2))", ct);
        await LockInvoiceAsync(request.InvoiceId, ct);
        var prior = await db.PaymentOperations.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct);
        if (prior is not null) return prior.PayloadHash == hash && prior.Action == "COLLECT"
            ? new(MutationOutcome.Success, await FindAsync(prior.PaymentId, ct)) : new(MutationOutcome.IdempotencyConflict);
        var invoice = await invoices.FindAsync(request.InvoiceId, ct);
        if (invoice is null) return new(MutationOutcome.NotFound);
        if (invoice.Status != "ISSUED") return new(MutationOutcome.Conflict);
        var paid = await db.Payments.Where(x => x.InvoiceId == request.InvoiceId && x.Status == PaymentStatus.CONFIRMED).SumAsync(x => x.Amount, ct);
        var amount = decimal.Round(request.Amount, 0, MidpointRounding.AwayFromZero);
        if (amount <= 0 || amount > invoice.TotalAmount - paid) return new(MutationOutcome.Overpayment);
        var now = DateTimeOffset.UtcNow;
        var number = $"PT-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..20]}";
        var p = new Payment(number, request.InvoiceId, amount,
            request.PaymentMethodCode, now, now, actorId, request.ReferenceNumber, request.Note);
        p.Confirm(actorId, invoice.TotalAmount - paid, now);
        db.Payments.Add(p);
        db.PaymentStatusHistory.Add(new PaymentStatusHistory(p.Id, PaymentStatus.CONFIRMED, actorId, now, PaymentStatus.PENDING, "Kế toán thu tiền đã kiểm tra"));
        db.PaymentOperations.Add(new PaymentOperation(key, p.Id, "COLLECT", hash, now));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(MutationOutcome.Success, await FindAsync(p.Id, ct));
    }
    private Task LockInvoiceAsync(Guid id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);
}
