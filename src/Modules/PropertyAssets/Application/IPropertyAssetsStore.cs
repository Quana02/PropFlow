using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;
using BuildingPage = PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Buildings.Building>;
using FacilityPage = PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Facilities.Facility>;
using EquipmentPage = PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment>;

namespace PropFlow.Modules.PropertyAssets.Application;

public sealed record BuildingAssetCounts(int Facilities, int ActiveFacilities, int Equipment, int ActiveEquipment);

public interface IPropertyAssetsStore
{
    Task<BuildingPage> BuildingsAsync(BuildingFilterQuery query, CancellationToken ct);
    Task<Building?> BuildingAsync(Guid id, bool tracking, CancellationToken ct);
    Task<BuildingAssetCounts> BuildingAssetCountsAsync(Guid id, CancellationToken ct);
    Task<bool> BuildingCodeExistsAsync(string code, CancellationToken ct);
    Task<bool> BuildingExistsAsync(Guid id, CancellationToken ct);
    Task<string?> BuildingNameAsync(Guid id, CancellationToken ct);
    void Add(Building building);

    Task<FacilityPage> FacilitiesAsync(FacilityFilterQuery query, CancellationToken ct);
    Task<Facility?> FacilityAsync(Guid id, bool tracking, bool includeEquipment, CancellationToken ct);
    Task<bool> FacilityCodeExistsAsync(string code, CancellationToken ct);
    void Add(Facility facility);

    Task<EquipmentPage> EquipmentAsync(EquipmentFilterQuery query, CancellationToken ct);
    Task<EquipmentEntity?> EquipmentAsync(Guid id, bool tracking, CancellationToken ct);
    Task<bool> EquipmentCodeExistsAsync(Guid buildingId, string code, CancellationToken ct);
    void Add(EquipmentEntity equipment);

    Task SaveAsync(CancellationToken ct);
}
