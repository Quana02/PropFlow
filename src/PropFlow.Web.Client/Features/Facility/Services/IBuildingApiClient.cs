using PropFlow.Web.Client.Features.Facility.Models;

namespace PropFlow.Web.Client.Features.Facility.Services;

public interface IBuildingApiClient
{
    Task<PagedResult<BuildingModel>> GetBuildingsAsync(BuildingFilterModel filter, CancellationToken cancellationToken = default);
    Task<BuildingModel?> GetBuildingByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BuildingDetailModel?> GetBuildingDetailByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BuildingModel> CreateBuildingAsync(CreateBuildingModel model, CancellationToken cancellationToken = default);
    Task<BuildingModel> UpdateBuildingAsync(Guid id, UpdateBuildingModel model, CancellationToken cancellationToken = default);
    Task<BuildingModel> SetBuildingStatusAsync(Guid id, MasterDataStatus status, CancellationToken cancellationToken = default);
}
