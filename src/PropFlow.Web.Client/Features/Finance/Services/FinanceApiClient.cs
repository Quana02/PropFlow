using PropFlow.Web.Client.Features.Finance.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Finance.Services;

public sealed class FinanceApiClient(AuthenticatedApiClient api) : IFinanceApiClient
{
    public Task<ApiResult<PagedFinanceResult<InvoiceBalance>>> GetUnpaidInvoicesAsync(int page, string? search = null, CancellationToken ct = default) =>
        api.SendAsync<PagedFinanceResult<InvoiceBalance>>(HttpMethod.Get, $"api/v1/billing/invoices/unpaid?page={page}&pageSize=20&search={Uri.EscapeDataString(search ?? string.Empty)}", ct: ct);
    public Task<ApiResult<PagedFinanceResult<InvoiceBalance>>> GetOverdueDebtsAsync(int page, string? search = null, CancellationToken ct = default) =>
        api.SendAsync<PagedFinanceResult<InvoiceBalance>>(HttpMethod.Get, $"api/v1/billing/debts/overdue?page={page}&pageSize=20&search={Uri.EscapeDataString(search ?? string.Empty)}", ct: ct);
    public Task<ApiResult<PagedFinanceResult<PaymentRow>>> GetPendingPaymentsAsync(int page, string? search = null, CancellationToken ct = default) =>
        api.SendAsync<PagedFinanceResult<PaymentRow>>(HttpMethod.Get, $"api/v1/payments/pending?page={page}&pageSize=20&search={Uri.EscapeDataString(search ?? string.Empty)}", ct: ct);
    public Task<ApiResult<PagedFinanceResult<PaymentRow>>> GetConfirmedPaymentsAsync(int page, string? search = null, CancellationToken ct = default) =>
        api.SendAsync<PagedFinanceResult<PaymentRow>>(HttpMethod.Get, $"api/v1/payments/confirmed?page={page}&pageSize=20&search={Uri.EscapeDataString(search ?? string.Empty)}", ct: ct);
    public Task<ApiResult<PaymentRow>> ConfirmPaymentAsync(Guid id, string idempotencyKey, CancellationToken ct = default) =>
        api.SendAsync<PaymentRow>(HttpMethod.Post, $"api/v1/payments/{id}/confirm", headers: Headers(idempotencyKey), ct: ct);
    public Task<ApiResult<PaymentRow>> RejectPaymentAsync(Guid id, string reason, string idempotencyKey, CancellationToken ct = default) =>
        api.SendAsync<PaymentRow>(HttpMethod.Post, $"api/v1/payments/{id}/reject", new RejectPaymentRequest(reason), headers: Headers(idempotencyKey), ct: ct);
    private static Dictionary<string, string> Headers(string key) => new() { ["Idempotency-Key"] = key };
}
