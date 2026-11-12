using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;

public record FacilityDto(
    Guid Id,
    string Code,
    string Name,
    Guid? FacilityTypeId,
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
    Guid? FacilityTypeId,
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
    [StringLength(512)] string? Code,
    [Required(ErrorMessage = "Tên cơ sở vật chất là bắt buộc."), StringLength(150, ErrorMessage = "Tên cơ sở vật chất không được vượt quá 150 ký tự.")] string Name,
    Guid? FacilityTypeId = null,
    [StringLength(255, ErrorMessage = "Vị trí không được vượt quá 255 ký tự.")] string? LocationDescription = null,
    string? Description = null,
    [property: JsonIgnore] Guid? CreatedBy = null);

public record UpdateFacilityCommand(
    [Required(ErrorMessage = "Tên cơ sở vật chất là bắt buộc."), StringLength(150, ErrorMessage = "Tên cơ sở vật chất không được vượt quá 150 ký tự.")] string Name,
    Guid? FacilityTypeId = null,
    [StringLength(255, ErrorMessage = "Vị trí không được vượt quá 255 ký tự.")] string? LocationDescription = null,
    string? Description = null,
    [property: JsonIgnore] Guid? UpdatedBy = null);

public record SetFacilityStatusCommand(
    MasterDataStatus Status,
    [property: JsonIgnore] Guid? UpdatedBy = null);

public record FacilityFilterQuery(
    string? SearchKeyword = null,
    MasterDataStatus? Status = null,
    Guid? FacilityTypeId = null,
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

public record FacilityTypeDto(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record CreateFacilityTypeCommand(
    [Required(ErrorMessage = "Tên loại cơ sở vật chất là bắt buộc."), StringLength(80, MinimumLength = 1, ErrorMessage = "Tên loại cơ sở vật chất phải từ 1 đến 80 ký tự.")] string Name,
    [property: JsonIgnore] Guid? CreatedBy = null);
