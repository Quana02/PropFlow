namespace PropFlow.Modules.Billing.Contracts;

public sealed record InvoiceFinancialSnapshot(Guid Id, string InvoiceNumber, Guid ApartmentUnitId,
    DateOnly BillingPeriodStart, DateOnly BillingPeriodEnd, DateOnly? IssueDate, DateOnly? DueDate,
    decimal TotalAmount, string Status, string? InvoiceType = null, string? ApartmentNumber = null, string? ResidentName = null);

public interface IInvoiceFinancialSource
{
    Task<bool> IsRecipientAsync(Guid invoiceId, Guid userId, CancellationToken ct) => Task.FromResult(false);
    Task<InvoiceFinancialSnapshot?> FindAsync(Guid invoiceId, CancellationToken ct);
    async Task<IReadOnlyList<InvoiceFinancialSnapshot>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = new List<InvoiceFinancialSnapshot>();
        foreach (var id in ids.Distinct()) if (await FindAsync(id, ct) is { } invoice) result.Add(invoice);
        return result;
    }
    Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct);
}
