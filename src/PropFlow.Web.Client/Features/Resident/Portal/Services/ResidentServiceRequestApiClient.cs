using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

public interface IResidentServiceRequestApiClient
{
    Task<ApiResult<IReadOnlyList<ResidentServiceRequestListItem>>> ListAsync(
        ResidentServiceRequestListQuery? query = null,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ResidentServiceRequestDetailResponse>> GetAsync(
        Guid serviceRequestId,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ResidentServiceRequestCreatedResponse>> CreateAsync(
        CreateResidentServiceRequestRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ResidentServiceRequestFeedbackResponse>> RateAsync(
        Guid serviceRequestId,
        RateResidentServiceRequestRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ResidentServiceRequestApiClient(AuthenticatedApiClient api)
    : IResidentServiceRequestApiClient
{
    public Task<ApiResult<IReadOnlyList<ResidentServiceRequestListItem>>> ListAsync(
        ResidentServiceRequestListQuery? query = null,
        CancellationToken cancellationToken = default) =>
        api.SendAsync<IReadOnlyList<ResidentServiceRequestListItem>>(
            HttpMethod.Get,
            BuildListUri(query),
            ct: cancellationToken);

    private static string BuildListUri(ResidentServiceRequestListQuery? query)
    {
        const string path = "api/v1/resident/service-requests";
        if (query is null) return path;

        var parameters = new List<string>();
        Add(parameters, "search", query.Search);
        Add(parameters, "status", query.Status);
        Add(parameters, "fromDate", query.FromDate?.ToString("yyyy-MM-dd"));
        Add(parameters, "toDate", query.ToDate?.ToString("yyyy-MM-dd"));
        return parameters.Count == 0 ? path : $"{path}?{string.Join("&", parameters)}";
    }

    private static void Add(ICollection<string> parameters, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parameters.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
    }

    public Task<ApiResult<ResidentServiceRequestDetailResponse>> GetAsync(
        Guid serviceRequestId,
        CancellationToken cancellationToken = default) =>
        api.SendAsync<ResidentServiceRequestDetailResponse>(
            HttpMethod.Get,
            $"api/v1/resident/service-requests/{serviceRequestId}",
            ct: cancellationToken);

    public Task<ApiResult<ResidentServiceRequestCreatedResponse>> CreateAsync(
        CreateResidentServiceRequestRequest request,
        CancellationToken cancellationToken = default) =>
        api.SendAsync<ResidentServiceRequestCreatedResponse>(
            HttpMethod.Post,
            "api/v1/resident/service-requests",
            request,
            ct: cancellationToken);

    public Task<ApiResult<ResidentServiceRequestFeedbackResponse>> RateAsync(
        Guid serviceRequestId,
        RateResidentServiceRequestRequest request,
        CancellationToken cancellationToken = default) =>
        api.SendAsync<ResidentServiceRequestFeedbackResponse>(
            HttpMethod.Put,
            $"api/v1/resident/service-requests/{serviceRequestId}/feedback",
            request,
            ct: cancellationToken);
}
