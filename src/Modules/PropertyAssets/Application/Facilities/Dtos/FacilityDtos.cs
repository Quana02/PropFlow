using PropFlow.Modules.PropertyAssets.Domain.Buildings;

namespace PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;

public record FacilityDto(
    Guid Id,
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
    string Code,
    string Name,
    string? FacilityType = null,
    string? LocationDescription = null,
    string? Description = null,
    Guid? CreatedBy = null);

public record UpdateFacilityCommand(
    string Name,
    string? FacilityType = null,
    string? LocationDescription = null,
    string? Description = null,
    Guid? UpdatedBy = null);

public record SetFacilityStatusCommand(
    MasterDataStatus Status,
    Guid? UpdatedBy = null);

public record FacilityFilterQuery(
    string? SearchKeyword = null,
    MasterDataStatus? Status = null,
    string? FacilityType = null,
    int PageIndex = 1,
    int PageSize = 10);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageIndex,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;
}
