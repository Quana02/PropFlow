using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Resident.Services;

public interface IResidentServiceRequestApiClient
{
    Task<ApiResult<ResidentServiceRequestCreatedResponse>> CreateAsync(
        CreateResidentServiceRequestRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ResidentServiceRequestApiClient(AuthenticatedApiClient api)
    : IResidentServiceRequestApiClient
{
    public Task<ApiResult<ResidentServiceRequestCreatedResponse>> CreateAsync(
        CreateResidentServiceRequestRequest request,
        CancellationToken cancellationToken = default) =>
        api.SendAsync<ResidentServiceRequestCreatedResponse>(
            HttpMethod.Post,
            "api/v1/resident/service-requests",
            request,
            ct: cancellationToken);
}
