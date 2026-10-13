using PropFlow.Web.Client.Features.Finance.Models;
using PropFlow.Web.Client.Services.Api;
namespace PropFlow.Web.Client.Features.Finance.Services;
public sealed class BillingWorkspaceApi(AuthenticatedApiClient api)
{
    private const string Root = "api/v1/billing/workspace/";
    public Task<ApiResult<T>> GetAsync<T>(string path) => api.SendAsync<T>(HttpMethod.Get, Root + path);
    public Task<ApiResult<T>> SaveAsync<T>(string path, object body, HttpMethod? method = null) => api.SendAsync<T>(method ?? HttpMethod.Post, Root + path, body);
    public Task<ApiResult<FileResponse>> FileAsync(string path) => api.SendAsync<FileResponse>(HttpMethod.Get, Root + path);
    public Task<ApiResult<PaymentRow>> RecordPaymentAsync(object body, string key) => api.SendAsync<PaymentRow>(HttpMethod.Post, "api/v1/payments", body,
        headers: new Dictionary<string, string> { ["Idempotency-Key"] = key });
    public Task<ApiResult<PaymentRow[]>> PaymentsAsync(Guid id) => api.SendAsync<PaymentRow[]>(HttpMethod.Get, $"api/v1/payments/by-invoice/{id}");
    public static string Query(InvoiceFilter f) => $"Search={Uri.EscapeDataString(f.Search ?? "")}&Status={f.Status}&ApartmentId={f.ApartmentId}&From={f.From:yyyy-MM-dd}&To={f.To:yyyy-MM-dd}&Settlement={f.Settlement}";
}
