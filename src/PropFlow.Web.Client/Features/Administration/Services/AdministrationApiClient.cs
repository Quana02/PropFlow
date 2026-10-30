using PropFlow.Web.Client.Features.Administration.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Administration.Services;

public sealed class AdministrationApiClient(AuthenticatedApiClient api) : IAdministrationApiClient
{
    public Task<ApiResult<SePayConnectionState>> GetSePayConnectionAsync(CancellationToken ct = default) =>
        api.SendAsync<SePayConnectionState>(HttpMethod.Get, "api/v1/payment-settings/sepay/connection", ct: ct);
    public Task<ApiResult<SePayConnectionState>> CheckSePayConnectionAsync(CancellationToken ct = default) =>
        api.SendAsync<SePayConnectionState>(HttpMethod.Post, "api/v1/payment-settings/sepay/connection/check", ct: ct);
    public Task<ApiResult<SePayBankAccountView[]>> GetSePayAccountsAsync(CancellationToken ct = default) =>
        api.SendAsync<SePayBankAccountView[]>(HttpMethod.Get, "api/v1/payment-settings/sepay/accounts", ct: ct);
    public Task<ApiResult<PaymentDestinationPreview>> PreviewSePayAccountAsync(SelectSePayAccountRequest request, CancellationToken ct = default) =>
        api.SendAsync<PaymentDestinationPreview>(HttpMethod.Post, "api/v1/payment-settings/sepay/accounts/preview", request, ct: ct);
    public Task<ApiResult<PrivilegedActionChallengeResult>> RequestSePayAccountChallengeAsync(SelectSePayAccountRequest request, CancellationToken ct = default) =>
        api.SendAsync<PrivilegedActionChallengeResult>(HttpMethod.Post, "api/v1/payment-settings/sepay/accounts/change-challenge", request, ct: ct);
    public Task<ApiResult<PaymentDestinationView>> SaveSePayAccountAsync(ConfirmSePayAccountRequest request, CancellationToken ct = default) =>
        api.SendAsync<PaymentDestinationView>(HttpMethod.Put, "api/v1/payment-settings/sepay/accounts/destination", request, ct: ct);
    public Task<ApiResult<AdministrationOverview>> GetOverviewAsync(CancellationToken ct = default) =>
        api.SendAsync<AdministrationOverview>(HttpMethod.Get, "api/v1/reporting/administration-overview", ct: ct);
    public Task<ApiResult<InternalAccountSummary>> GetSummaryAsync(CancellationToken ct = default) => api.SendAsync<InternalAccountSummary>(HttpMethod.Get, "api/v1/administration/internal-accounts/summary", ct: ct);
    public Task<ApiResult<PagedInternalAccounts>> GetAccountsAsync(string role, string? search, string? status, int page, CancellationToken ct = default) => api.SendAsync<PagedInternalAccounts>(HttpMethod.Get, $"api/v1/administration/internal-accounts?role={Uri.EscapeDataString(role)}&search={Uri.EscapeDataString(search ?? string.Empty)}&status={Uri.EscapeDataString(status ?? string.Empty)}&page={page}&pageSize=20", ct: ct);
    public Task<ApiResult<InternalAccount>> CreateAsync(CreateInternalAccount request, CancellationToken ct = default) => api.SendAsync<InternalAccount>(HttpMethod.Post, "api/v1/administration/internal-accounts", request, ct: ct);
    public async Task<ApiResult> ChangeRoleAsync(Guid userId, string role, CancellationToken ct = default) { var result = await api.SendAsync<EmptyResponse>(HttpMethod.Put, $"api/v1/administration/internal-accounts/{userId}/role", new ChangeRole(role), ct: ct); return new(result.IsSuccess, result.StatusCode, result.Code, result.Message, result.TraceId, result.ValidationErrors); }
    public async Task<ApiResult> ChangeStatusAsync(Guid userId, string status, CancellationToken ct = default) { var result = await api.SendAsync<EmptyResponse>(HttpMethod.Put, $"api/v1/administration/internal-accounts/{userId}/status", new ChangeStatus(status), ct: ct); return new(result.IsSuccess, result.StatusCode, result.Code, result.Message, result.TraceId, result.ValidationErrors); }
    public Task<ApiResult<AdministrationRoleAccess[]>> GetRolesAsync(CancellationToken ct = default) =>
        api.SendAsync<AdministrationRoleAccess[]>(HttpMethod.Get, "api/v1/administration/roles", ct: ct);
    public Task<ApiResult<InternalAccountAccess>> GetEffectiveAccessAsync(Guid userId, CancellationToken ct = default) =>
        api.SendAsync<InternalAccountAccess>(HttpMethod.Get, $"api/v1/administration/internal-accounts/{userId}/effective-access", ct: ct);
    public Task<ApiResult<PagedAdministrationActivities>> GetActivityAsync(string? action, int page, CancellationToken ct = default) =>
        api.SendAsync<PagedAdministrationActivities>(HttpMethod.Get, $"api/v1/administration/activity?action={Uri.EscapeDataString(action ?? string.Empty)}&page={page}&pageSize=20", ct: ct);
    public Task<ApiResult<PaymentDestinationState>> GetPaymentDestinationAsync(CancellationToken ct = default) =>
        api.SendAsync<PaymentDestinationState>(HttpMethod.Get, "api/v1/payment-settings/destination", ct: ct);
    public Task<ApiResult<PaymentDestinationView[]>> GetPaymentDestinationHistoryAsync(CancellationToken ct = default) =>
        api.SendAsync<PaymentDestinationView[]>(HttpMethod.Get, "api/v1/payment-settings/destination/history", ct: ct);
    public Task<ApiResult<BankOption[]>> GetPaymentBanksAsync(CancellationToken ct = default) =>
        api.SendAsync<BankOption[]>(HttpMethod.Get, "api/v1/payment-settings/banks", ct: ct);
    public Task<ApiResult<PaymentDestinationPreview>> PreviewPaymentDestinationAsync(PaymentDestinationDraft request, CancellationToken ct = default) =>
        api.SendAsync<PaymentDestinationPreview>(HttpMethod.Post, "api/v1/payment-settings/destination/preview", request, ct: ct);
    public Task<ApiResult<PrivilegedActionChallengeResult>> RequestPaymentDestinationChallengeAsync(PaymentDestinationChallengeRequest request, CancellationToken ct = default) =>
        api.SendAsync<PrivilegedActionChallengeResult>(HttpMethod.Post, "api/v1/payment-settings/destination/change-challenge", request, ct: ct);
    public Task<ApiResult<PaymentDestinationView>> SavePaymentDestinationAsync(ConfirmPaymentDestinationRequest request, CancellationToken ct = default) =>
        api.SendAsync<PaymentDestinationView>(HttpMethod.Put, "api/v1/payment-settings/destination", request, ct: ct);
    public async Task<ApiResult> DeactivatePaymentDestinationAsync(DeactivatePaymentDestinationRequest request, CancellationToken ct = default)
    {
        var result = await api.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/payment-settings/destination/deactivate", request, ct: ct);
        return new(result.IsSuccess, result.StatusCode, result.Code, result.Message, result.TraceId, result.ValidationErrors);
    }
}
