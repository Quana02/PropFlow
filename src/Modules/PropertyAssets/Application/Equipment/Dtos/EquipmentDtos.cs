using PropFlow.Modules.PropertyAssets.Domain.Equipment;

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
    Guid BuildingId,
    string BuildingName,
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

public record EquipmentDetailDto(
    Guid Id,
    Guid BuildingId,
    string BuildingName,
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
    Guid BuildingId,
    string Code,
    string Name,
    Guid? FacilityId = null,
    string? EquipmentType = null,
    string? Manufacturer = null,
    string? Model = null,
    string? SerialNumber = null,
    DateOnly? InstallationDate = null,
    DateOnly? WarrantyExpiryDate = null,
    string? LocationDescription = null,
    string? Description = null,
    Guid? CreatedBy = null);

public record UpdateEquipmentCommand(
    string Name,
    Guid? FacilityId = null,
    string? EquipmentType = null,
    EquipmentStatus? Status = null,
    string? Manufacturer = null,
    string? Model = null,
    string? SerialNumber = null,
    DateOnly? InstallationDate = null,
    DateOnly? WarrantyExpiryDate = null,
    string? LocationDescription = null,
    string? Description = null,
    Guid? UpdatedBy = null);

public record SetEquipmentStatusCommand(
    EquipmentStatus Status,
    Guid? UpdatedBy = null);

public record EquipmentFilterQuery(
    Guid? BuildingId = null,
    Guid? FacilityId = null,
    string? SearchKeyword = null,
    EquipmentStatus? Status = null,
    int PageIndex = 1,
    int PageSize = 10);
