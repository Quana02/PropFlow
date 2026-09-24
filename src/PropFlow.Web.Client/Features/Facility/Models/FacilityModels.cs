using System.ComponentModel.DataAnnotations;

namespace PropFlow.Web.Client.Features.Facility.Models;

public class FacilityModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? FacilityType { get; set; }
    public string? LocationDescription { get; set; }
    public string? Description { get; set; }
    public MasterDataStatus Status { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class FacilityDetailModel : FacilityModel
{
    public int TotalEquipment { get; set; }
    public int ActiveEquipment { get; set; }
}

public class CreateFacilityModel
{
    [Required(ErrorMessage = "Mã cơ sở vật chất là bắt buộc.")]
    [StringLength(30, ErrorMessage = "Mã cơ sở vật chất không được vượt quá 30 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên cơ sở vật chất là bắt buộc.")]
    [StringLength(150, ErrorMessage = "Tên cơ sở vật chất không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(80, ErrorMessage = "Loại cơ sở vật chất không được vượt quá 80 ký tự.")]
    public string? FacilityType { get; set; }

    [StringLength(255, ErrorMessage = "Vị trí không được vượt quá 255 ký tự.")]
    public string? LocationDescription { get; set; }

    [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
    public string? Description { get; set; }
}

public class UpdateFacilityModel
{
    [Required(ErrorMessage = "Tên cơ sở vật chất là bắt buộc.")]
    [StringLength(150, ErrorMessage = "Tên cơ sở vật chất không được vượt quá 150 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(80, ErrorMessage = "Loại cơ sở vật chất không được vượt quá 80 ký tự.")]
    public string? FacilityType { get; set; }

    [StringLength(255, ErrorMessage = "Vị trí không được vượt quá 255 ký tự.")]
    public string? LocationDescription { get; set; }

    [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
    public string? Description { get; set; }
}

public class FacilityFilterModel
{
    public string? SearchKeyword { get; set; }
    public MasterDataStatus? Status { get; set; }
    public string? FacilityType { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
public class SetFacilityStatusModel
{
    public MasterDataStatus Status { get; set; }
}
