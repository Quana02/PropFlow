using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public interface IBuildingApiClient
{
    Task<ApiResult<PagedResult<BuildingModel>>> GetBuildingsAsync(BuildingFilterModel filter, CancellationToken cancellationToken = default);
    Task<ApiResult<BuildingModel>> GetBuildingByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResult<BuildingDetailModel>> GetBuildingDetailByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResult<BuildingModel>> CreateBuildingAsync(CreateBuildingModel model, CancellationToken cancellationToken = default);
    Task<ApiResult<BuildingModel>> UpdateBuildingAsync(Guid id, UpdateBuildingModel model, CancellationToken cancellationToken = default);
    Task<ApiResult<BuildingModel>> SetBuildingStatusAsync(Guid id, MasterDataStatus status, CancellationToken cancellationToken = default);
}
