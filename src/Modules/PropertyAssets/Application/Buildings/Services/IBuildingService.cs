using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;

namespace PropFlow.Modules.PropertyAssets.Application.Buildings.Services;

public interface IBuildingService
{
    Task<PagedResult<BuildingDto>> GetBuildingsAsync(BuildingFilterQuery query, CancellationToken cancellationToken = default);
    Task<BuildingDto?> GetBuildingByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BuildingDetailDto?> GetBuildingDetailByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BuildingDto> CreateBuildingAsync(CreateBuildingCommand command, CancellationToken cancellationToken = default);
    Task<BuildingDto> UpdateBuildingAsync(Guid id, UpdateBuildingCommand command, CancellationToken cancellationToken = default);
    Task<BuildingDto> SetBuildingStatusAsync(Guid id, SetBuildingStatusCommand command, CancellationToken cancellationToken = default);
}
