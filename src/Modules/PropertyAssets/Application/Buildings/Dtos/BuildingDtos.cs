using PropFlow.Modules.PropertyAssets.Domain.Buildings;

namespace PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;

public record BuildingDto(
    Guid Id,
    string Code,
    string Name,
    string Address,
    string TimeZoneId,
    int? NumberOfFloors,
    string? Description,
    MasterDataStatus Status,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateBuildingCommand(
    string Code,
    string Name,
    string Address,
    string TimeZoneId = "Asia/Ho_Chi_Minh",
    int? NumberOfFloors = null,
    string? Description = null,
    Guid? CreatedBy = null);

public record UpdateBuildingCommand(
    string Name,
    string Address,
    string TimeZoneId = "Asia/Ho_Chi_Minh",
    int? NumberOfFloors = null,
    string? Description = null,
    Guid? UpdatedBy = null);

public record SetBuildingStatusCommand(
    MasterDataStatus Status,
    Guid? UpdatedBy = null);

public record BuildingFilterQuery(
    string? SearchKeyword = null,
    MasterDataStatus? Status = null,
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
