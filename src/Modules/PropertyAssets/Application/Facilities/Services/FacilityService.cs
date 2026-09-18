using PropFlow.Modules.PropertyAssets.Application;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;

namespace PropFlow.Modules.PropertyAssets.Application.Facilities.Services;

public class FacilityService : IFacilityService
{
    private readonly IPropertyAssetsStore _store;

    public FacilityService(IPropertyAssetsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PagedResult<FacilityDto>> GetFacilitiesAsync(FacilityFilterQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _store.FacilitiesAsync(query, cancellationToken);
        var items = page.Items.Select(f => new FacilityDto(
                f.Id,
                f.BuildingId,
                f.Building != null ? f.Building.Name : string.Empty,
                f.Code,
                f.Name,
                f.FacilityType,
                f.LocationDescription,
                f.Description,
                f.Status,
                f.CreatedBy,
                f.UpdatedBy,
                f.CreatedAt,
                f.UpdatedAt)).ToList();

        return new PagedResult<FacilityDto>(items, page.TotalCount, page.PageIndex, page.PageSize);
    }

    public async Task<FacilityDetailDto?> GetFacilityByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var facility = await _store.FacilityAsync(id, false, true, cancellationToken);

        if (facility == null)
            return null;

        var totalEq = facility.Equipment.Count;
        var activeEq = facility.Equipment.Count(e => e.Status == PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentStatus.ACTIVE);

        return new FacilityDetailDto(
            facility.Id,
            facility.BuildingId,
            facility.Building != null ? facility.Building.Name : string.Empty,
            facility.Code,
            facility.Name,
            facility.FacilityType,
            facility.LocationDescription,
            facility.Description,
            facility.Status,
            totalEq,
            activeEq,
            facility.CreatedBy,
            facility.UpdatedBy,
            facility.CreatedAt,
            facility.UpdatedAt);
    }

    public async Task<FacilityDto> CreateFacilityAsync(CreateFacilityCommand command, CancellationToken cancellationToken = default)
    {
        var buildingExists = await _store.BuildingExistsAsync(command.BuildingId, cancellationToken);
        if (!buildingExists)
        {
            throw new ArgumentException("Tòa nhà không tồn tại.");
        }

        var codeExists = await _store.FacilityCodeExistsAsync(command.Code.Trim(), cancellationToken);
        if (codeExists)
        {
            throw new ArgumentException($"Mã tiện ích '{command.Code}' đã tồn tại.");
        }

        var facility = new Facility(
            command.BuildingId,
            command.Code,
            command.Name,
            DateTimeOffset.UtcNow,
            command.FacilityType,
            command.LocationDescription,
            command.Description,
            command.CreatedBy,
            command.InitialStatus);

        _store.Add(facility);
        await _store.SaveAsync(cancellationToken);

        return await MapToDtoAsync(facility, cancellationToken);
    }

    public async Task<FacilityDto> UpdateFacilityAsync(Guid id, UpdateFacilityCommand command, CancellationToken cancellationToken = default)
    {
        var facility = await _store.FacilityAsync(id, true, true, cancellationToken);

        if (facility == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tiện ích.");
        }

        // Validate Building exists
        var buildingExists = await _store.BuildingExistsAsync(command.BuildingId, cancellationToken);
        if (!buildingExists)
        {
            throw new ArgumentException("Tòa nhà không tồn tại.");
        }

        // Prevent moving Facility to a different Building if it has Equipment
        if (facility.BuildingId != command.BuildingId && facility.Equipment.Any())
        {
            throw new InvalidOperationException($"Không thể chuyển cơ sở vật chất '{facility.Name}' sang tòa nhà khác vì có {facility.Equipment.Count} thiết bị đang gắn với cơ sở vật chất này. Vui lòng chuyển hoặc xóa các thiết bị trước khi thay đổi tòa nhà.");
        }

        facility.Update(
            command.BuildingId,
            command.Name,
            command.FacilityType,
            command.LocationDescription,
            command.Description,
            command.Status,
            command.UpdatedBy,
            DateTimeOffset.UtcNow);

        await _store.SaveAsync(cancellationToken);

        return await MapToDtoAsync(facility, cancellationToken);
    }

    public async Task<FacilityDto> SetFacilityStatusAsync(Guid id, SetFacilityStatusCommand command, CancellationToken cancellationToken = default)
    {
        var facility = await _store.FacilityAsync(id, true, false, cancellationToken);

        if (facility == null)
        {
            throw new KeyNotFoundException("Không tìm thấy tiện ích.");
        }

        facility.SetStatus(command.Status, command.UpdatedBy, DateTimeOffset.UtcNow);

        await _store.SaveAsync(cancellationToken);

        return await MapToDtoAsync(facility, cancellationToken);
    }

    private async Task<FacilityDto> MapToDtoAsync(Facility facility, CancellationToken cancellationToken)
    {
        var buildingName = facility.BuildingId == Guid.Empty
            ? string.Empty
            : await _store.BuildingNameAsync(facility.BuildingId, cancellationToken) ?? string.Empty;

        return new FacilityDto(
            facility.Id,
            facility.BuildingId,
            buildingName,
            facility.Code,
            facility.Name,
            facility.FacilityType,
            facility.LocationDescription,
            facility.Description,
            facility.Status,
            facility.CreatedBy,
            facility.UpdatedBy,
            facility.CreatedAt,
            facility.UpdatedAt);
    }
}
