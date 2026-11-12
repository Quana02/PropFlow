using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
namespace PropFlow.Modules.Payments.Infrastructure.Finance;
public sealed class PaymentNoticeSource(PaymentsDbContext db) : IPaymentNoticeSource
{
    private static readonly System.Linq.Expressions.Expression<Func<PropFlow.Modules.Payments.Domain.Payments.Payment, PaymentNoticeSnapshot>> Map = x =>
        new PaymentNoticeSnapshot(x.Id, x.InvoiceId, x.PaymentNumber, x.Amount, x.PaymentMethodCode,
            x.Status.ToString(), x.PaymentDate, x.ConfirmedAt, x.RejectionReason);
    public async Task<IReadOnlyList<PaymentNoticeSnapshot>> ListAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct) =>
        await db.Payments.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId)).Select(Map).ToArrayAsync(ct);
    public Task<PaymentNoticeSnapshot?> FindAsync(Guid id, CancellationToken ct) => db.Payments.AsNoTracking().Where(x => x.Id == id).Select(Map).SingleOrDefaultAsync(ct);
}
