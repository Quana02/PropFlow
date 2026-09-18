using PropFlow.Modules.PropertyAssets.Domain.Buildings;

namespace PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;

public record BuildingDetailDto(
    Guid Id,
    string Code,
    string Name,
    string Address,
    string TimeZoneId,
    int? NumberOfFloors,
    string? Description,
    MasterDataStatus Status,
    int FacilityCount,
    int EquipmentCount,
    int ActiveFacilityCount,
    int ActiveEquipmentCount,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
