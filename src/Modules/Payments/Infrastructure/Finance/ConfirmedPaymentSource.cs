using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.Modules.Payments.Infrastructure.Finance;

public sealed class ConfirmedPaymentSource(PaymentsDbContext db) : IConfirmedPaymentSource
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> TotalsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct) =>
        await db.Payments.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId) && x.Status == PaymentStatus.CONFIRMED)
            .GroupBy(x => x.InvoiceId).Select(x => new { x.Key, Amount = x.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Amount, ct);

    public Task<decimal> TotalAsync(Guid invoiceId, CancellationToken ct) => db.Payments.AsNoTracking()
        .Where(x => x.InvoiceId == invoiceId && x.Status == PaymentStatus.CONFIRMED)
        .SumAsync(x => x.Amount, ct);
}
