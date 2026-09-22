using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;
using FacilityPage = PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Facilities.Facility>;
using EquipmentPage = PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment>;

namespace PropFlow.Modules.PropertyAssets.Application;

public interface IPropertyAssetsStore
{
    Task<Building?> BuildingAsync(Guid id, bool tracking, CancellationToken ct);
    Task<CurrentBuildingPropertyOverviewDto?> CurrentBuildingOverviewAsync(CancellationToken ct);
    void Add(Building building);

    Task<FacilityPage> FacilitiesAsync(FacilityFilterQuery query, CancellationToken ct);
    Task<Facility?> FacilityAsync(Guid id, bool tracking, bool includeEquipment, CancellationToken ct);
    Task<bool> FacilityCodeExistsAsync(string code, CancellationToken ct);
    void Add(Facility facility);

    Task<EquipmentPage> EquipmentAsync(EquipmentFilterQuery query, CancellationToken ct);
    Task<EquipmentEntity?> EquipmentAsync(Guid id, bool tracking, CancellationToken ct);
    Task<bool> EquipmentCodeExistsAsync(string code, CancellationToken ct);
    void Add(EquipmentEntity equipment);

    Task SaveAsync(CancellationToken ct);
}
