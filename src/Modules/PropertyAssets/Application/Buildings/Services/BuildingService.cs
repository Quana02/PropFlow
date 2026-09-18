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

    public async Task<PagedResult<BuildingDto>> GetBuildingsAsync(BuildingFilterQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _store.BuildingsAsync(query, cancellationToken);
        return new PagedResult<BuildingDto>(page.Items.Select(MapToDto).ToList(), page.TotalCount, page.PageIndex, page.PageSize);
    }

    public async Task<BuildingDto?> GetBuildingByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var building = await _store.BuildingAsync(id, false, cancellationToken);

        return building is null ? null : MapToDto(building);
    }

    public async Task<BuildingDetailDto?> GetBuildingDetailByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var building = await _store.BuildingAsync(id, false, cancellationToken);

        if (building is null) return null;

        var counts = await _store.BuildingAssetCountsAsync(id, cancellationToken);

        return new BuildingDetailDto(
            building.Id,
            building.Code,
            building.Name,
            building.Address,
            building.TimeZoneId,
            building.NumberOfFloors,
            building.Description,
            building.Status,
            counts.Facilities,
            counts.Equipment,
            counts.ActiveFacilities,
            counts.ActiveEquipment,
            building.CreatedBy,
            building.UpdatedBy,
            building.CreatedAt,
            building.UpdatedAt);
    }

    public async Task<BuildingDto> CreateBuildingAsync(CreateBuildingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var codeUpper = command.Code.Trim();
        var existingBuilding = await _store.BuildingCodeExistsAsync(codeUpper, cancellationToken);

        if (existingBuilding)
        {
            throw new InvalidOperationException($"Mã tòa nhà '{command.Code}' đã tồn tại trong hệ thống.");
        }

        var now = DateTimeOffset.UtcNow;
        var building = new Building(
            command.Code,
            command.Name,
            command.Address,
            now,
            string.IsNullOrWhiteSpace(command.TimeZoneId) ? "Asia/Ho_Chi_Minh" : command.TimeZoneId,
            command.NumberOfFloors,
            command.Description,
            command.CreatedBy);

        _store.Add(building);
        await _store.SaveAsync(cancellationToken);

        return MapToDto(building);
    }

    public async Task<BuildingDto> UpdateBuildingAsync(Guid id, UpdateBuildingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var building = await _store.BuildingAsync(id, true, cancellationToken);
        if (building is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy tòa nhà với mã định danh ID = {id}.");
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

    public async Task<BuildingDto> SetBuildingStatusAsync(Guid id, SetBuildingStatusCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var building = await _store.BuildingAsync(id, true, cancellationToken);
        if (building is null)
        {
            throw new KeyNotFoundException($"Không tìm thấy tòa nhà với mã định danh ID = {id}.");
        }

        var now = DateTimeOffset.UtcNow;
        if (command.Status == MasterDataStatus.INACTIVE)
        {
            building.Deactivate(command.UpdatedBy, now);
        }
        else
        {
            building.Activate(command.UpdatedBy, now);
        }

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
