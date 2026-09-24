using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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
    [Required(ErrorMessage = "Mã cơ sở vật chất là bắt buộc."), StringLength(30, ErrorMessage = "Mã cơ sở vật chất không được vượt quá 30 ký tự.")] string Code,
    [Required(ErrorMessage = "Tên cơ sở vật chất là bắt buộc."), StringLength(150, ErrorMessage = "Tên cơ sở vật chất không được vượt quá 150 ký tự.")] string Name,
    [StringLength(80, ErrorMessage = "Loại cơ sở vật chất không được vượt quá 80 ký tự.")] string? FacilityType = null,
    [StringLength(255, ErrorMessage = "Vị trí không được vượt quá 255 ký tự.")] string? LocationDescription = null,
    string? Description = null,
    [property: JsonIgnore] Guid? CreatedBy = null);

public record UpdateFacilityCommand(
    [Required(ErrorMessage = "Tên cơ sở vật chất là bắt buộc."), StringLength(150, ErrorMessage = "Tên cơ sở vật chất không được vượt quá 150 ký tự.")] string Name,
    [StringLength(80, ErrorMessage = "Loại cơ sở vật chất không được vượt quá 80 ký tự.")] string? FacilityType = null,
    [StringLength(255, ErrorMessage = "Vị trí không được vượt quá 255 ký tự.")] string? LocationDescription = null,
    string? Description = null,
    [property: JsonIgnore] Guid? UpdatedBy = null);

public record SetFacilityStatusCommand(
    MasterDataStatus Status,
    [property: JsonIgnore] Guid? UpdatedBy = null);

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
