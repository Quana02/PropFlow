using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Persistence;

namespace PropFlow.Modules.Billing.Infrastructure.Finance;

public sealed class InvoiceFinancialSource(BillingDbContext db) : IInvoiceFinancialSource
{
    public Task<InvoiceFinancialSnapshot?> FindAsync(Guid invoiceId, CancellationToken ct) => db.Invoices.AsNoTracking()
        .Where(x => x.Id == invoiceId).Select(x => new InvoiceFinancialSnapshot(x.Id, x.InvoiceNumber, x.ApartmentUnitId,
            x.BillingPeriodStart, x.BillingPeriodEnd, x.IssueDate, x.DueDate, x.TotalAmount, x.Status.ToString())).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct) =>
        await db.Invoices.AsNoTracking().Where(x => x.Status == InvoiceStatus.ISSUED)
            .Select(x => new InvoiceFinancialSnapshot(x.Id, x.InvoiceNumber, x.ApartmentUnitId,
                x.BillingPeriodStart, x.BillingPeriodEnd, x.IssueDate, x.DueDate, x.TotalAmount, x.Status.ToString())).ToArrayAsync(ct);
}
