using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public sealed class BuildingApiClient(AuthenticatedApiClient api) : IBuildingApiClient
{
    public Task<ApiResult<CurrentBuildingOverviewResponse>> GetCurrentBuildingOverviewAsync(CancellationToken cancellationToken = default) =>
        api.SendAsync<CurrentBuildingOverviewResponse>(HttpMethod.Get, "api/v1/buildings/current", ct: cancellationToken);

    public Task<ApiResult<BuildingModel>> UpdateCurrentBuildingAsync(UpdateCurrentBuildingModel model, CancellationToken cancellationToken = default) =>
        api.SendAsync<BuildingModel>(HttpMethod.Put, "api/v1/buildings/current", model, ct: cancellationToken);

}
