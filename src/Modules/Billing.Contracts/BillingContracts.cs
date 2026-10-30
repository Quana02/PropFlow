namespace PropFlow.Modules.Billing.Contracts;

public sealed record InvoiceFinancialSnapshot(Guid Id, string InvoiceNumber, Guid ApartmentUnitId,
    DateOnly BillingPeriodStart, DateOnly BillingPeriodEnd, DateOnly? IssueDate, DateOnly? DueDate,
    decimal TotalAmount, string Status, string? InvoiceType = null, string? ApartmentNumber = null, string? ResidentName = null);

public interface IInvoiceFinancialSource
{
    Task<InvoiceFinancialSnapshot?> FindAsync(Guid invoiceId, CancellationToken ct);
    async Task<IReadOnlyList<InvoiceFinancialSnapshot>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = new List<InvoiceFinancialSnapshot>();
        foreach (var id in ids.Distinct()) if (await FindAsync(id, ct) is { } invoice) result.Add(invoice);
        return result;
    }
    Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct);
}

public sealed record ServiceRequestInvoiceCommand(
    Guid ServiceRequestId,
    string RequestNumber,
    Guid ApartmentUnitId,
    Guid ResidentId,
    Guid? ResidentUserId,
    string ResidentName,
    decimal BaseAmount,
    decimal AdditionalAmount,
    string? AdditionalNote,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAt);

public sealed record ServiceRequestSettlementSnapshot(
    Guid ServiceRequestId,
    Guid InvoiceId,
    string InvoiceNumber,
    decimal TotalAmount,
    decimal ConfirmedPaidAmount,
    decimal OutstandingAmount,
    string Status);

public interface IServiceRequestBilling
{
    Task<ServiceRequestSettlementSnapshot?> IssueAsync(ServiceRequestInvoiceCommand command, CancellationToken ct);
    Task<ServiceRequestSettlementSnapshot?> FindForResidentAsync(Guid serviceRequestId, Guid residentId, CancellationToken ct);
    Task<ServiceRequestSettlementSnapshot?> FindForUserAsync(Guid serviceRequestId, Guid userId, CancellationToken ct);
}
