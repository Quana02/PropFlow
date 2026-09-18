using PropFlow.Modules.PropertyAssets.Domain.Buildings;

namespace PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;

public record FacilityDto(
    Guid Id,
    Guid BuildingId,
    string BuildingName,
    string Code,
    string Name,
    string? FacilityType,
    string? LocationDescription,
    string? Description,
    MasterDataStatus Status,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record FacilityDetailDto(
    Guid Id,
    Guid BuildingId,
    string BuildingName,
    string Code,
    string Name,
    string? FacilityType,
    string? LocationDescription,
    string? Description,
    MasterDataStatus Status,
    int TotalEquipment,
    int ActiveEquipment,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateFacilityCommand(
    Guid BuildingId,
    string Code,
    string Name,
    string? FacilityType = null,
    string? LocationDescription = null,
    string? Description = null,
    Guid? CreatedBy = null,
    MasterDataStatus InitialStatus = MasterDataStatus.ACTIVE);

public record UpdateFacilityCommand(
    Guid BuildingId,
    string Name,
    string? FacilityType = null,
    string? LocationDescription = null,
    string? Description = null,
    MasterDataStatus? Status = null,
    Guid? UpdatedBy = null);

public record SetFacilityStatusCommand(
    MasterDataStatus Status,
    Guid? UpdatedBy = null);

public record FacilityFilterQuery(
    Guid? BuildingId = null,
    string? SearchKeyword = null,
    MasterDataStatus? Status = null,
    int PageIndex = 1,
    int PageSize = 10);
