using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.ServiceRequests.Services;

public interface IManagerServiceRequestApiClient
{
    Task<ApiResult<ManagerServiceRequestListResponse>> ListAsync(ManagerServiceRequestListQuery query, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> GetAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestClassificationView>> ClassificationAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestClassificationView>> AnalyzeClassificationAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestClassificationView>> ReviewClassificationAsync(Guid id, ReviewManagerServiceRequestClassification request, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestRecommendationView>> RecommendationAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestRecommendationView>> AnalyzeRecommendationAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestRecommendationView>> ReviewRecommendationAsync(Guid id, ReviewManagerServiceRequestRecommendation request, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> ReviewAsync(Guid id, ReviewManagerServiceRequestRequest request, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<ServiceRequestMaintenanceReference>>> SearchLinkCandidatesAsync(string? search, int limit = 20, CancellationToken ct = default);
    Task<ApiResult<ManagerPropertyContextOptionsResponse>> PropertyContextOptionsAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> ConfirmPropertyContextAsync(Guid id, ConfirmManagerServiceRequestPropertyContextRequest request, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestAssignmentOptionsResponse>> AssignmentOptionsAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> AssignAsync(Guid id, AssignManagerServiceRequestRequest request, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> RequestReworkAsync(Guid id, ReviewManagerServiceRequestResolutionRequest request, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> ApproveResolutionAsync(Guid id, ReviewManagerServiceRequestResolutionRequest request, CancellationToken ct = default);
    Task<ApiResult<ManagerServiceRequestDetailResponse>> CloseAsync(Guid id, CloseManagerServiceRequestRequest request, CancellationToken ct = default);
    Task<ApiResult<ApiFileContent>> DownloadEvidenceAsync(string downloadUrl, CancellationToken ct = default);
}

public sealed class ManagerServiceRequestApiClient(AuthenticatedApiClient api) : IManagerServiceRequestApiClient
{
    public Task<ApiResult<ManagerServiceRequestListResponse>> ListAsync(ManagerServiceRequestListQuery query, CancellationToken ct = default)
    {
        var values = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}", $"unassignedOnly={query.UnassignedOnly.ToString().ToLowerInvariant()}" };
        Add(values, "search", query.Search);
        Add(values, "status", query.Status);
        Add(values, "priority", query.Priority);
        if (query.SubmittedWithinDays.HasValue) values.Add($"submittedWithinDays={query.SubmittedWithinDays.Value}");
        return api.SendAsync<ManagerServiceRequestListResponse>(HttpMethod.Get,
            $"api/v1/manager/service-requests?{string.Join("&", values)}", ct: ct);
    }

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> GetAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Get, $"api/v1/manager/service-requests/{id}", ct: ct);

    public Task<ApiResult<ManagerServiceRequestClassificationView>> ClassificationAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestClassificationView>(HttpMethod.Get, $"api/v1/manager/service-requests/{id}/classification", ct: ct);

    public Task<ApiResult<ManagerServiceRequestClassificationView>> AnalyzeClassificationAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestClassificationView>(HttpMethod.Post, $"api/v1/manager/service-requests/{id}/classification", ct: ct);

    public Task<ApiResult<ManagerServiceRequestClassificationView>> ReviewClassificationAsync(Guid id, ReviewManagerServiceRequestClassification request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestClassificationView>(HttpMethod.Put, $"api/v1/manager/service-requests/{id}/classification/review", request, ct: ct);

    public Task<ApiResult<ManagerServiceRequestRecommendationView>> RecommendationAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestRecommendationView>(HttpMethod.Get, $"api/v1/manager/service-requests/{id}/recommendation", ct: ct);

    public Task<ApiResult<ManagerServiceRequestRecommendationView>> AnalyzeRecommendationAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestRecommendationView>(HttpMethod.Post, $"api/v1/manager/service-requests/{id}/recommendation", ct: ct);

    public Task<ApiResult<ManagerServiceRequestRecommendationView>> ReviewRecommendationAsync(Guid id, ReviewManagerServiceRequestRecommendation request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestRecommendationView>(HttpMethod.Put, $"api/v1/manager/service-requests/{id}/recommendation/review", request, ct: ct);

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> ReviewAsync(Guid id, ReviewManagerServiceRequestRequest request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Post, $"api/v1/manager/service-requests/{id}/review", request, ct: ct);

    public Task<ApiResult<IReadOnlyList<ServiceRequestMaintenanceReference>>> SearchLinkCandidatesAsync(string? search, int limit = 20, CancellationToken ct = default) =>
        api.SendAsync<IReadOnlyList<ServiceRequestMaintenanceReference>>(HttpMethod.Get,
            $"api/v1/manager/service-requests/link-candidates?limit={limit}&search={Uri.EscapeDataString(search?.Trim() ?? string.Empty)}", ct: ct);
    public Task<ApiResult<ManagerPropertyContextOptionsResponse>> PropertyContextOptionsAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<ManagerPropertyContextOptionsResponse>(HttpMethod.Get, $"api/v1/manager/service-requests/{id}/property-context/options", ct: ct);

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> ConfirmPropertyContextAsync(Guid id, ConfirmManagerServiceRequestPropertyContextRequest request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Put, $"api/v1/manager/service-requests/{id}/property-context", request, ct: ct);

    public Task<ApiResult<ManagerServiceRequestAssignmentOptionsResponse>> AssignmentOptionsAsync(Guid id, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestAssignmentOptionsResponse>(HttpMethod.Get, $"api/v1/manager/service-requests/{id}/assignment/options", ct: ct);

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> AssignAsync(Guid id, AssignManagerServiceRequestRequest request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Put, $"api/v1/manager/service-requests/{id}/assignment", request, ct: ct);

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> RequestReworkAsync(Guid id, ReviewManagerServiceRequestResolutionRequest request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Post, $"api/v1/manager/service-requests/{id}/resolution/rework", request, ct: ct);

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> ApproveResolutionAsync(Guid id, ReviewManagerServiceRequestResolutionRequest request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Post, $"api/v1/manager/service-requests/{id}/resolution/approve", request, ct: ct);

    public Task<ApiResult<ManagerServiceRequestDetailResponse>> CloseAsync(Guid id, CloseManagerServiceRequestRequest request, CancellationToken ct = default) =>
        api.SendAsync<ManagerServiceRequestDetailResponse>(HttpMethod.Post, $"api/v1/manager/service-requests/{id}/close", request, ct: ct);

    public Task<ApiResult<ApiFileContent>> DownloadEvidenceAsync(string downloadUrl, CancellationToken ct = default) =>
        api.GetFileAsync(downloadUrl.TrimStart('/'), ct);

    private static void Add(ICollection<string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values.Add($"{key}={Uri.EscapeDataString(value.Trim())}");
    }
}
