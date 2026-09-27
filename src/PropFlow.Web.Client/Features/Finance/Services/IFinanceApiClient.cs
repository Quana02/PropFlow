using PropFlow.Web.Client.Features.Finance.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Finance.Services;

public interface IFinanceApiClient
{
    Task<ApiResult<PagedFinanceResult<InvoiceBalance>>> GetUnpaidInvoicesAsync(int page, string? search = null, CancellationToken ct = default);
    Task<ApiResult<PagedFinanceResult<InvoiceBalance>>> GetOverdueDebtsAsync(int page, string? search = null, CancellationToken ct = default);
    Task<ApiResult<PagedFinanceResult<PaymentRow>>> GetPendingPaymentsAsync(int page, string? search = null, CancellationToken ct = default);
    Task<ApiResult<PagedFinanceResult<PaymentRow>>> GetConfirmedPaymentsAsync(int page, string? search = null, CancellationToken ct = default);
    Task<ApiResult<PaymentRow>> ConfirmPaymentAsync(Guid id, string idempotencyKey, CancellationToken ct = default);
    Task<ApiResult<PaymentRow>> RejectPaymentAsync(Guid id, string reason, string idempotencyKey, CancellationToken ct = default);
}
