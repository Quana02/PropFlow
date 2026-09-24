using System.ComponentModel.DataAnnotations;

namespace PropFlow.Web.Client.Features.Facility.Models;

public enum EquipmentStatus { ACTIVE, INACTIVE, OUT_OF_SERVICE, UNDER_MAINTENANCE }

public class EquipmentModel
{
    public Guid Id { get; set; }
    public Guid? FacilityId { get; set; }
    public string? FacilityName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? EquipmentType { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? InstallationDate { get; set; }
    public DateOnly? WarrantyExpiryDate { get; set; }
    public string? LocationDescription { get; set; }
    public EquipmentStatus Status { get; set; }
    public string? Description { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class EquipmentDetailModel : EquipmentModel
{
}

public class CreateEquipmentModel
{
    [Required(ErrorMessage = "Mã thiết bị là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Mã thiết bị không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên thiết bị là bắt buộc.")]
    [StringLength(150, ErrorMessage = "Tên thiết bị không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    public Guid? FacilityId { get; set; }

    [Required(ErrorMessage = "Phân loại thiết bị là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Loại thiết bị không được vượt quá 100 ký tự.")]
    public string? EquipmentType { get; set; }

    public EquipmentStatus Status { get; set; } = EquipmentStatus.ACTIVE;

    [StringLength(100, ErrorMessage = "Hãng sản xuất không được vượt quá 100 ký tự.")]
    public string? Manufacturer { get; set; }

    [StringLength(100, ErrorMessage = "Model không được vượt quá 100 ký tự.")]
    public string? Model { get; set; }

    [StringLength(100, ErrorMessage = "Số serial không được vượt quá 100 ký tự.")]
    public string? SerialNumber { get; set; }

    public DateOnly? InstallationDate { get; set; }

    public DateOnly? WarrantyExpiryDate { get; set; }

    [StringLength(255, ErrorMessage = "Vị trí không được vượt quá 255 ký tự.")]
    public string? LocationDescription { get; set; }

    [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
    public string? Description { get; set; }
}

public class UpdateEquipmentModel
{
    [Required(ErrorMessage = "Tên thiết bị là bắt buộc.")]
    [StringLength(150, ErrorMessage = "Tên thiết bị không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    public Guid? FacilityId { get; set; }

    [Required(ErrorMessage = "Phân loại thiết bị là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Loại thiết bị không được vượt quá 100 ký tự.")]
    public string? EquipmentType { get; set; }

    [StringLength(100, ErrorMessage = "Hãng sản xuất không được vượt quá 100 ký tự.")]
    public string? Manufacturer { get; set; }

    [StringLength(100, ErrorMessage = "Model không được vượt quá 100 ký tự.")]
    public string? Model { get; set; }

    [StringLength(100, ErrorMessage = "Số serial không được vượt quá 100 ký tự.")]
    public string? SerialNumber { get; set; }

    public DateOnly? InstallationDate { get; set; }

    public DateOnly? WarrantyExpiryDate { get; set; }

    [StringLength(255, ErrorMessage = "Vị trí không được vượt quá 255 ký tự.")]
    public string? LocationDescription { get; set; }

    [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
    public string? Description { get; set; }
}

public class SetEquipmentStatusModel
{
    public EquipmentStatus Status { get; set; }
}

public class EquipmentFilterModel
{
    public Guid? FacilityId { get; set; }
    public string? SearchKeyword { get; set; }
    public EquipmentStatus? Status { get; set; }
    public string? EquipmentType { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
