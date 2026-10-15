namespace PropFlow.Modules.Payments.Application.Finance;

public sealed record PaymentRow(Guid Id, string PaymentNumber, Guid InvoiceId, decimal Amount, string PaymentMethodCode,
    DateTimeOffset PaymentDate, string? ReferenceNumber, string? Note, string Status, DateTimeOffset? ConfirmedAt, string? InvoiceNumber = null, string? ApartmentNumber = null, string? ResidentName = null);
public sealed record PagedPayments(int Total, int Page, int PageSize, IReadOnlyList<PaymentRow> Items, IReadOnlyDictionary<string,int>? Counts = null);
public enum MutationOutcome { Success, NotFound, Conflict, IdempotencyConflict, Overpayment }
public sealed record MutationResult(MutationOutcome Outcome, PaymentRow? Payment = null);
public sealed record CollectPaymentRequest(Guid InvoiceId, decimal Amount, string PaymentMethodCode,
    string? ReferenceNumber, string? Note);
public interface IPaymentFinanceStore
{
    Task<IReadOnlyList<PaymentRow>> ByInvoiceAsync(Guid invoiceId, CancellationToken ct);
    Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string sortDirection, CancellationToken ct, string? filter = null);
    Task<PaymentRow?> FindAsync(Guid id, CancellationToken ct);
    Task<MutationResult> ChangeAsync(Guid id, string action, string? reason, string key, Guid actorId, CancellationToken ct);
    Task<MutationResult> CollectAsync(CollectPaymentRequest request, string key, Guid actorId, CancellationToken ct);
}
public sealed class PaymentFinanceUseCases(IPaymentFinanceStore store)
{
    public Task<IReadOnlyList<PaymentRow>> ByInvoiceAsync(Guid invoiceId, CancellationToken ct) => store.ByInvoiceAsync(invoiceId, ct);
    public Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string sortDirection, CancellationToken ct, string? filter = null) =>
        store.ListAsync(status, search, Math.Max(1, page), Math.Clamp(pageSize, 1, 100), sortDirection, ct, filter);
    public Task<PaymentRow?> DetailAsync(Guid id, CancellationToken ct) => store.FindAsync(id, ct);
    public Task<MutationResult> ConfirmAsync(Guid id, string key, Guid actor, CancellationToken ct) => store.ChangeAsync(id, "CONFIRM", null, key, actor, ct);
    public Task<MutationResult> RejectAsync(Guid id, string reason, string key, Guid actor, CancellationToken ct) => store.ChangeAsync(id, "REJECT", reason, key, actor, ct);
    public Task<MutationResult> CollectAsync(CollectPaymentRequest request, string key, Guid actor, CancellationToken ct) => store.CollectAsync(request, key, actor, ct);
}
