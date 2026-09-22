using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;

namespace PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;

public record BuildingDto(
    Guid Id,
    string Code,
    string Name,
    string Address,
    string TimeZoneId,
    int NumberOfFloors,
    string? Description,
    MasterDataStatus Status,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record FacilitySummaryDto(
    int Total,
    int Active,
    int UnderMaintenance,
    int Inactive,
    int Unavailable,
    int OutOfService);

public record EquipmentSummaryDto(
    int Total,
    int Active,
    int UnderMaintenance,
    int Inactive,
    int OutOfService);

public record CurrentBuildingPropertyOverviewDto(
    Guid Id,
    string Code,
    string Name,
    string Address,
    string TimeZoneId,
    int NumberOfFloors,
    string? Description,
    MasterDataStatus Status,
    FacilitySummaryDto Facilities,
    EquipmentSummaryDto Equipment,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateCurrentBuildingCommand(
    string Name,
    string Address,
    string TimeZoneId = "Asia/Ho_Chi_Minh",
    int NumberOfFloors = 1,
    string? Description = null,
    Guid? UpdatedBy = null);
