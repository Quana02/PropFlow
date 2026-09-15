using System.ComponentModel.DataAnnotations;

namespace PropFlow.Web.Client.Features.Facility.Models;

public enum MasterDataStatus
{
    ACTIVE,
    INACTIVE
}

public class BuildingModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public int? NumberOfFloors { get; set; }
    public string? Description { get; set; }
    public MasterDataStatus Status { get; set; } = MasterDataStatus.ACTIVE;
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CreateBuildingModel
{
    [Required(ErrorMessage = "Mã tòa nhà là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Mã tòa nhà không được vượt quá 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên tòa nhà là bắt buộc.")]
    [StringLength(200, ErrorMessage = "Tên tòa nhà không được vượt quá 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ tòa nhà là bắt buộc.")]
    [StringLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Múi giờ là bắt buộc.")]
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    [Range(1, 200, ErrorMessage = "Số tầng phải từ 1 đến 200.")]
    public int? NumberOfFloors { get; set; }

    [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
    public string? Description { get; set; }
}

public class UpdateBuildingModel
{
    [Required(ErrorMessage = "Tên tòa nhà là bắt buộc.")]
    [StringLength(200, ErrorMessage = "Tên tòa nhà không được vượt quá 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ tòa nhà là bắt buộc.")]
    [StringLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Múi giờ là bắt buộc.")]
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    [Range(1, 200, ErrorMessage = "Số tầng phải từ 1 đến 200.")]
    public int? NumberOfFloors { get; set; }

    [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
    public string? Description { get; set; }
}

public class SetBuildingStatusModel
{
    public MasterDataStatus Status { get; set; }
}

public class BuildingFilterModel
{
    public string? SearchKeyword { get; set; }
    public MasterDataStatus? Status { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
}
