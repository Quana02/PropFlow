using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;

namespace PropFlow.Modules.PropertyAssets.Application.Buildings.Services;

public class BuildingService : IBuildingService
{
    private readonly IPropertyAssetsStore _store;

    public BuildingService(IPropertyAssetsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<CurrentBuildingPropertyOverviewDto?> GetCurrentBuildingOverviewAsync(CancellationToken cancellationToken = default)
    {
        return await _store.CurrentBuildingOverviewAsync(cancellationToken);
    }

    public async Task<BuildingDto> UpdateCurrentBuildingAsync(UpdateCurrentBuildingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.NumberOfFloors <= 0)
        {
            throw new ArgumentException("Tổng số tầng phải lớn hơn 0.");
        }

        var overview = await _store.CurrentBuildingOverviewAsync(cancellationToken);
        if (overview is null)
        {
            if (string.IsNullOrWhiteSpace(command.Code))
            {
                throw new ArgumentException("Mã chung cư là bắt buộc khi thiết lập hồ sơ ban đầu.");
            }

            var createdAt = DateTimeOffset.UtcNow;
            var newBuilding = new Building(
                command.Code,
                command.Name,
                command.Address,
                createdAt,
                string.IsNullOrWhiteSpace(command.TimeZoneId) ? "Asia/Ho_Chi_Minh" : command.TimeZoneId,
                command.NumberOfFloors,
                command.Description,
                command.UpdatedBy);

            _store.Add(newBuilding);
            await _store.SaveAsync(cancellationToken);
            return MapToDto(newBuilding);
        }

        var building = await _store.BuildingAsync(overview.Id, true, cancellationToken);
        if (building is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy tòa nhà với mã định danh ID = {overview.Id}.");
        }

        var now = DateTimeOffset.UtcNow;
        building.Update(
            command.Name,
            command.Address,
            string.IsNullOrWhiteSpace(command.TimeZoneId) ? "Asia/Ho_Chi_Minh" : command.TimeZoneId,
            command.NumberOfFloors,
            command.Description,
            command.UpdatedBy,
            now);

        await _store.SaveAsync(cancellationToken);

        return MapToDto(building);
    }

    private static BuildingDto MapToDto(Building building)
    {
        return new BuildingDto(
            building.Id,
            building.Code,
            building.Name,
            building.Address,
            building.TimeZoneId,
            building.NumberOfFloors,
            building.Description,
            building.Status,
            building.CreatedBy,
            building.UpdatedBy,
            building.CreatedAt,
            building.UpdatedAt);
    }
}
