using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Infrastructure.Finance;

namespace PropFlow.Api.Composition;

// After the payment transaction commits, reuse Billing's existing reconciliation and outbox.
// A delivery failure never undoes a recorded collection; the existing worker retries it.
public sealed class PaymentFinanceNotifications(IPaymentFinanceStore store, IBillingCycle cycle, ILogger<PaymentFinanceNotifications> logger) : IPaymentFinanceStore
{
    public async Task<MutationResult> SubmitAsync(CollectPaymentRequest request,string key,Guid actor,CancellationToken ct) => await Notify(await store.SubmitAsync(request,key,actor,ct),ct);
    public Task<IReadOnlyList<PaymentRow>> ByInvoiceAsync(Guid invoiceId, CancellationToken ct) => store.ByInvoiceAsync(invoiceId, ct);
    public Task<PagedPayments> ListAsync(string status,string? search,int page,int pageSize,string sort,CancellationToken ct,string? filter = null) => store.ListAsync(status,search,page,pageSize,sort,ct,filter);
    public Task<PaymentRow?> FindAsync(Guid id,CancellationToken ct) => store.FindAsync(id,ct);
    public async Task<MutationResult> ChangeAsync(Guid id,string action,string? reason,string key,Guid actor,CancellationToken ct) => await Notify(await store.ChangeAsync(id,action,reason,key,actor,ct),ct);
    public async Task<MutationResult> CollectAsync(CollectPaymentRequest request,string key,Guid actor,CancellationToken ct) => await Notify(await store.CollectAsync(request,key,actor,ct),ct);
    public async Task<MutationResult> CollectVerifiedBankAsync(CollectPaymentRequest request,string key,Guid actor,CancellationToken ct) => await Notify(await store.CollectVerifiedBankAsync(request,key,actor,ct),ct);
    private async Task<MutationResult> Notify(MutationResult result,CancellationToken ct) {
        if(result.Outcome == MutationOutcome.Success && result.Payment is { } payment)
            try { await cycle.NotifyPaymentAsync(payment.InvoiceId,ct); }
            catch(Exception ex) { logger.LogWarning("Payment notification deferred for {PaymentId} ({Type}).",payment.Id,ex.GetType().Name); }
        return result;
    }
}
