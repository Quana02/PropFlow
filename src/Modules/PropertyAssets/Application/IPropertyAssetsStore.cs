using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;
using EquipmentTypeEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentType;
using FacilityPage = PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Facilities.Facility>;
using EquipmentPage = PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos.PagedResult<PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment>;

namespace PropFlow.Modules.PropertyAssets.Application;

public sealed class IdentifierCodeConflictException : InvalidOperationException
{
    public IdentifierCodeConflictException() : base("Mã tự động sinh đã tồn tại.") { }
}

public interface IPropertyAssetsStore
{
    Task<Building?> BuildingAsync(Guid id, bool tracking, CancellationToken ct);
    Task<CurrentBuildingPropertyOverviewDto?> CurrentBuildingOverviewAsync(CancellationToken ct);
    void Add(Building building);

    Task<FacilityPage> FacilitiesAsync(FacilityFilterQuery query, CancellationToken ct);
    Task<Facility?> FacilityAsync(Guid id, bool tracking, bool includeEquipment, CancellationToken ct);
    Task<bool> FacilityCodeExistsAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<string>> FacilityCodesWithPrefixAsync(string prefix, CancellationToken ct);
    void Add(Facility facility);
    Task<IReadOnlyList<FacilityType>> FacilityTypesAsync(bool activeOnly, CancellationToken ct);
    Task<FacilityType?> FacilityTypeAsync(Guid id, bool tracking, CancellationToken ct);
    Task<bool> FacilityTypeNameExistsAsync(string normalizedName, CancellationToken ct);
    void Add(FacilityType facilityType);

    Task<EquipmentPage> EquipmentAsync(EquipmentFilterQuery query, CancellationToken ct);
    Task<EquipmentEntity?> EquipmentAsync(Guid id, bool tracking, CancellationToken ct);
    Task<bool> EquipmentCodeExistsAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<string>> EquipmentCodesWithPrefixAsync(string prefix, CancellationToken ct);
    void Add(EquipmentEntity equipment);
    Task<IReadOnlyList<EquipmentTypeEntity>> EquipmentTypesAsync(bool activeOnly, CancellationToken ct);
    Task<EquipmentTypeEntity?> EquipmentTypeAsync(Guid id, bool tracking, CancellationToken ct);
    Task<bool> EquipmentTypeNameExistsAsync(string normalizedName, CancellationToken ct);
    void Add(EquipmentTypeEntity equipmentType);

    Task<T> WithAdvisoryLockAsync<T>(string scope, Func<CancellationToken, Task<T>> action, CancellationToken ct);
    void Detach(object entity);

    Task SaveAsync(CancellationToken ct);
    Task SaveGeneratedCodeAsync(CancellationToken ct);
}
