namespace PropFlow.Modules.Payments.Application.Finance;

public sealed record PaymentRow(Guid Id, string PaymentNumber, Guid InvoiceId, decimal Amount, string PaymentMethodCode,
    DateTimeOffset PaymentDate, string? ReferenceNumber, string? Note, string Status, DateTimeOffset? ConfirmedAt);
public sealed record PagedPayments(int Total, int Page, int PageSize, IReadOnlyList<PaymentRow> Items);
public enum MutationOutcome { Success, NotFound, Conflict, IdempotencyConflict, Overpayment }
public sealed record MutationResult(MutationOutcome Outcome, PaymentRow? Payment = null);
public interface IPaymentFinanceStore
{
    Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string sortDirection, CancellationToken ct);
    Task<PaymentRow?> FindAsync(Guid id, CancellationToken ct);
    Task<MutationResult> ChangeAsync(Guid id, string action, string? reason, string key, Guid actorId, CancellationToken ct);
}
public sealed class PaymentFinanceUseCases(IPaymentFinanceStore store)
{
    public Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string sortDirection, CancellationToken ct) =>
        store.ListAsync(status, search, Math.Max(1, page), Math.Clamp(pageSize, 1, 100), sortDirection, ct);
    public Task<PaymentRow?> DetailAsync(Guid id, CancellationToken ct) => store.FindAsync(id, ct);
    public Task<MutationResult> ConfirmAsync(Guid id, string key, Guid actor, CancellationToken ct) => store.ChangeAsync(id, "CONFIRM", null, key, actor, ct);
    public Task<MutationResult> RejectAsync(Guid id, string reason, string key, Guid actor, CancellationToken ct) => store.ChangeAsync(id, "REJECT", reason, key, actor, ct);
}
