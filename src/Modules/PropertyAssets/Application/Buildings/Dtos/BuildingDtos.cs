using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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
    [Required(ErrorMessage = "Tên chung cư là bắt buộc."), StringLength(150, ErrorMessage = "Tên chung cư không được vượt quá 150 ký tự.")] string Name,
    [Required(ErrorMessage = "Địa chỉ chung cư là bắt buộc.")] string Address,
    [Required, StringLength(64, ErrorMessage = "Múi giờ không được vượt quá 64 ký tự.")] string TimeZoneId = "Asia/Ho_Chi_Minh",
    [Range(1, int.MaxValue, ErrorMessage = "Tổng số tầng phải lớn hơn 0.")] int NumberOfFloors = 1,
    string? Description = null,
    [property: JsonIgnore] Guid? UpdatedBy = null,
    [StringLength(50, ErrorMessage = "Mã chung cư không được vượt quá 50 ký tự.")] string? Code = null);
