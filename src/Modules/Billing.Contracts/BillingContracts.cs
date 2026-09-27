namespace PropFlow.Modules.Billing.Contracts;

public sealed record InvoiceFinancialSnapshot(Guid Id, string InvoiceNumber, Guid ApartmentUnitId,
    DateOnly BillingPeriodStart, DateOnly BillingPeriodEnd, DateOnly? IssueDate, DateOnly? DueDate,
    decimal TotalAmount, string Status);

public interface IInvoiceFinancialSource
{
    Task<InvoiceFinancialSnapshot?> FindAsync(Guid invoiceId, CancellationToken ct);
    Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct);
}
