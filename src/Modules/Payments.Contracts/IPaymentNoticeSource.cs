namespace PropFlow.Modules.Payments.Contracts;
public sealed record PaymentNoticeSnapshot(Guid Id, Guid InvoiceId, string Number, decimal Amount,
    string Method, string Status, DateTimeOffset Date, DateTimeOffset? ConfirmedAt, string? RejectionReason);
public interface IPaymentNoticeSource
{
    Task<IReadOnlyList<PaymentNoticeSnapshot>> ListAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);
    Task<PaymentNoticeSnapshot?> FindAsync(Guid id, CancellationToken ct);
}
