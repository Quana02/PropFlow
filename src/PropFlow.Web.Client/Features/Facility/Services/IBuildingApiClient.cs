using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public interface IBuildingApiClient
{
    Task<ApiResult<CurrentBuildingOverviewResponse>> GetCurrentBuildingOverviewAsync(CancellationToken cancellationToken = default);
    Task<ApiResult<BuildingModel>> UpdateCurrentBuildingAsync(UpdateCurrentBuildingModel model, CancellationToken cancellationToken = default);
}
