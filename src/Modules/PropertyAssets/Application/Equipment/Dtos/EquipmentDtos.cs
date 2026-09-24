using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;

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

public record EquipmentDto(
    Guid Id,
    Guid? FacilityId,
    string? FacilityName,
    [property: Required(ErrorMessage = "Mã thiết bị là bắt buộc."), StringLength(50, ErrorMessage = "Mã thiết bị không được vượt quá 50 ký tự.")] string Code,
    [property: Required(ErrorMessage = "Tên thiết bị là bắt buộc."), StringLength(150, ErrorMessage = "Tên thiết bị không được vượt quá 150 ký tự.")] string Name,
    string? EquipmentType,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    DateOnly? InstallationDate,
    DateOnly? WarrantyExpiryDate,
    string? LocationDescription,
    EquipmentStatus Status,
    string? Description,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record EquipmentDetailDto(
    Guid Id,
    Guid? FacilityId,
    string? FacilityName,
    string Code,
    string Name,
    string? EquipmentType,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    DateOnly? InstallationDate,
    DateOnly? WarrantyExpiryDate,
    string? LocationDescription,
    EquipmentStatus Status,
    string? Description,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateEquipmentCommand(
    [Required(ErrorMessage = "Mã thiết bị là bắt buộc."), StringLength(50, ErrorMessage = "Mã thiết bị không được vượt quá 50 ký tự.")] string Code,
    [Required(ErrorMessage = "Tên thiết bị là bắt buộc."), StringLength(150, ErrorMessage = "Tên thiết bị không được vượt quá 150 ký tự.")] string Name,
    Guid? FacilityId = null,
    [Required(ErrorMessage = "Phân loại thiết bị là bắt buộc."), StringLength(100, ErrorMessage = "Phân loại thiết bị không được vượt quá 100 ký tự.")] string? EquipmentType = null,
    string? Manufacturer = null,
    string? Model = null,
    string? SerialNumber = null,
    DateOnly? InstallationDate = null,
    DateOnly? WarrantyExpiryDate = null,
    string? LocationDescription = null,
    string? Description = null,
    [property: JsonIgnore] Guid? CreatedBy = null);

public record UpdateEquipmentCommand(
    [Required(ErrorMessage = "Tên thiết bị là bắt buộc."), StringLength(150, ErrorMessage = "Tên thiết bị không được vượt quá 150 ký tự.")] string Name,
    Guid? FacilityId = null,
    [Required(ErrorMessage = "Phân loại thiết bị là bắt buộc."), StringLength(100, ErrorMessage = "Phân loại thiết bị không được vượt quá 100 ký tự.")] string? EquipmentType = null,
    string? Manufacturer = null,
    string? Model = null,
    string? SerialNumber = null,
    DateOnly? InstallationDate = null,
    DateOnly? WarrantyExpiryDate = null,
    string? LocationDescription = null,
    string? Description = null,
    [property: JsonIgnore] Guid? UpdatedBy = null);

public record SetEquipmentStatusCommand(
    EquipmentStatus Status,
    [property: JsonIgnore] Guid? UpdatedBy = null);

public record EquipmentFilterQuery(
    Guid? FacilityId = null,
    string? SearchKeyword = null,
    EquipmentStatus? Status = null,
    string? EquipmentType = null,
    int PageIndex = 1,
    int PageSize = 10);
