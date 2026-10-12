using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.ServiceRequests.Services;

public interface IManagerServiceRequestApiClient
{
    Task<ApiResult<ManagerServiceRequestListResponse>> ListAsync(ManagerServiceRequestListQuery query, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> GetAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> ReviewAsync(Guid id, ReviewManagerServiceRequestRequest request, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<ServiceRequestMaintenanceReference>>> SearchLinkCandidatesAsync(string? search, int limit = 20, CancellationToken ct = default);
}

public sealed class ManagerServiceRequestApiClient(AuthenticatedApiClient api) : IManagerServiceRequestApiClient
{
    public Task<ApiResult<ManagerServiceRequestListResponse>> ListAsync(ManagerServiceRequestListQuery query, CancellationToken ct = default)
    {
        var values = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}", $"unassignedOnly={query.UnassignedOnly.ToString().ToLowerInvariant()}" };
        Add(values, "search", query.Search);
        Add(values, "status", query.Status);
        Add(values, "priority", query.Priority);
        return api.SendAsync<ManagerServiceRequestListResponse>(HttpMethod.Get,
            $"api/v1/manager/service-requests?{string.Join("&", values)}", ct: ct);
    }

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> GetAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Get, $"api/v1/manager/service-requests/{id}", ct: ct);

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> ReviewAsync(Guid id, ReviewManagerServiceRequestRequest request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Post, $"api/v1/manager/service-requests/{id}/review", request, ct: ct);

    public Task<ApiResult<IReadOnlyList<ServiceRequestMaintenanceReference>>> SearchLinkCandidatesAsync(string? search, int limit = 20, CancellationToken ct = default) =>
        api.SendAsync<IReadOnlyList<ServiceRequestMaintenanceReference>>(HttpMethod.Get,
            $"api/v1/manager/service-requests/link-candidates?limit={limit}&search={Uri.EscapeDataString(search?.Trim() ?? string.Empty)}", ct: ct);

    private static void Add(ICollection<string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
    }
}
