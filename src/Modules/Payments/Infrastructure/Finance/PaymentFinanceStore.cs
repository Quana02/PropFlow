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
    public async Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string sortDirection, CancellationToken ct)
    {
        var parsed = Enum.Parse<PaymentStatus>(status, true); var q = db.Payments.AsNoTracking().Where(x => x.Status == parsed);
        if (!string.IsNullOrWhiteSpace(search)) { var s = search.Trim().ToUpper(); q = q.Where(x => x.PaymentNumber.Contains(s) || (x.ReferenceNumber != null && x.ReferenceNumber.Contains(s))); }
        var total = await q.CountAsync(ct); q = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) ? q.OrderBy(x => x.PaymentDate) : q.OrderByDescending(x => x.PaymentDate);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new PaymentRow(x.Id, x.PaymentNumber, x.InvoiceId, x.Amount, x.PaymentMethodCode, x.PaymentDate, x.ReferenceNumber, x.Note, x.Status.ToString(), x.ConfirmedAt)).ToArrayAsync(ct);
        return new(total, page, pageSize, items);
    }
    public Task<PaymentRow?> FindAsync(Guid id, CancellationToken ct) => db.Payments.AsNoTracking().Where(x => x.Id == id)
        .Select(x => new PaymentRow(x.Id, x.PaymentNumber, x.InvoiceId, x.Amount, x.PaymentMethodCode, x.PaymentDate, x.ReferenceNumber, x.Note, x.Status.ToString(), x.ConfirmedAt)).SingleOrDefaultAsync(ct);
    public async Task<MutationResult> ChangeAsync(Guid id, string action, string? reason, string key, Guid actorId, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{action}|{reason}")));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var prior = await db.PaymentOperations.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (prior is not null) return prior.PayloadHash == hash ? new(MutationOutcome.Success, await FindAsync(id, ct)) : new(MutationOutcome.IdempotencyConflict);
        var payment = await db.Payments.FromSqlInterpolated($"SELECT * FROM payments.payments WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (payment is null) return new(MutationOutcome.NotFound); if (payment.Status != PaymentStatus.PENDING) return new(MutationOutcome.Conflict);
        var invoice = await invoices.FindAsync(payment.InvoiceId, ct); if (invoice is null) return new(MutationOutcome.Conflict);
        if (action == "CONFIRM") { var paid = await db.Payments.Where(x => x.InvoiceId == payment.InvoiceId && x.Status == PaymentStatus.CONFIRMED).SumAsync(x => x.Amount, ct); if (payment.Amount > invoice.TotalAmount - paid) return new(MutationOutcome.Overpayment); payment.Confirm(actorId, invoice.TotalAmount - paid, DateTimeOffset.UtcNow); }
        else payment.Reject(actorId, reason!, DateTimeOffset.UtcNow);
        db.PaymentStatusHistory.Add(new PaymentStatusHistory(payment.Id, payment.Status, actorId, DateTimeOffset.UtcNow, PaymentStatus.PENDING, reason)); db.PaymentOperations.Add(new PaymentOperation(key, id, action, hash, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(MutationOutcome.Success, await FindAsync(id, ct));
    }
}
