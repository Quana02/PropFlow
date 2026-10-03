namespace PropFlow.Modules.Payments.Contracts;

public sealed record ConfirmedPaymentTotal(Guid InvoiceId, decimal Amount);

public interface IConfirmedPaymentSource
{
    Task<IReadOnlyDictionary<Guid, decimal>> TotalsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);
    Task<decimal> TotalAsync(Guid invoiceId, CancellationToken ct);
}
