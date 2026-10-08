using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

public interface IResidentServiceRequestApiClient
{
    Task<ApiResult<IReadOnlyList<ResidentServiceRequestListItem>>> ListAsync(
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
        CancellationToken cancellationToken = default) =>
        api.SendAsync<IReadOnlyList<ResidentServiceRequestListItem>>(
            HttpMethod.Get,
            "api/v1/resident/service-requests",
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
