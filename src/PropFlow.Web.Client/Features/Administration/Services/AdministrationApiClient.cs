using PropFlow.Web.Client.Features.Administration.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Administration.Services;

public sealed class AdministrationApiClient(AuthenticatedApiClient api) : IAdministrationApiClient
{
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
}
