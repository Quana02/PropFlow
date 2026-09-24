using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;

namespace PropFlow.Modules.PropertyAssets.Application.Buildings.Services;

public interface IBuildingService
{
    Task<CurrentBuildingPropertyOverviewDto?> GetCurrentBuildingOverviewAsync(CancellationToken cancellationToken = default);
    Task<BuildingDto> UpdateCurrentBuildingAsync(UpdateCurrentBuildingCommand command, CancellationToken cancellationToken = default);
}
