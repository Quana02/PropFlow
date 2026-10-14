using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Features.Finance.Models;
namespace PropFlow.Web.Client.Features.Finance.Services;
public sealed record ServiceRate(decimal UnitRate, DateOnly EffectiveFrom);
public sealed record ServiceRateRow(Guid Id, decimal UnitRate, DateOnly EffectiveFrom, Guid CreatedBy);
public sealed record HouseholdInvoice(Guid Id, string Number, string Apartment, string ResidentName,
    DateOnly Period, DateOnly? DueDate, decimal Total, decimal Paid, decimal Outstanding,
    decimal ServiceTotal = 0, decimal RentalTotal = 0, string Capacity = "HOUSEHOLD_HEAD", string? InvoiceType = null);
public sealed record ResidentPayment(Guid Id, Guid InvoiceId, string Number, decimal Amount, string Method,
    string Status, DateTimeOffset Date, DateTimeOffset? ConfirmedAt, string? RejectionReason);
public sealed record ResidentNotice(Guid Id, string Title, string Message, string? ActionPath, bool IsRead, DateTimeOffset CreatedAt);
public sealed record CollectionInput(Guid InvoiceId, decimal Amount, string PaymentMethodCode, string? ReferenceNumber, string? Note);
public sealed record ReminderResult(int Recipients);
public sealed class FinanceCycleApi(AuthenticatedApiClient api)
{
    public Task<ApiResult<T>> GetAsync<T>(string path) => api.SendAsync<T>(HttpMethod.Get, path);
    public Task<ApiResult<T>> PostAsync<T>(string path, object body) => api.SendAsync<T>(HttpMethod.Post, path, body);
    public Task<ApiResult<PaymentRow>> CollectAsync(CollectionInput input, string key) =>
        api.SendAsync<PaymentRow>(HttpMethod.Post, "api/v1/payments/collect", input, headers: new Dictionary<string,string> { ["Idempotency-Key"] = key });
}
