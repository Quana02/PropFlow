using System.ComponentModel.DataAnnotations;

namespace PropFlow.Web.Client.Features.Facility.Models;

public enum MasterDataStatus
{
    ACTIVE,
    INACTIVE,
    UNDER_MAINTENANCE,
    UNAVAILABLE,
    OUT_OF_SERVICE
}

public class BuildingModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public int NumberOfFloors { get; set; } = 1;
    public string? Description { get; set; }
    public MasterDataStatus Status { get; set; } = MasterDataStatus.ACTIVE;
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class FacilitySummaryModel
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int UnderMaintenance { get; set; }
    public int Inactive { get; set; }
    public int Unavailable { get; set; }
    public int OutOfService { get; set; }
}

public class EquipmentSummaryModel
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int UnderMaintenance { get; set; }
    public int Inactive { get; set; }
    public int OutOfService { get; set; }
}

public class CurrentBuildingOverviewResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public int NumberOfFloors { get; set; } = 1;
    public string? Description { get; set; }
    public MasterDataStatus Status { get; set; } = MasterDataStatus.ACTIVE;
    public int TotalApartments { get; set; }
    public FacilitySummaryModel Facilities { get; set; } = new();
    public EquipmentSummaryModel Equipment { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UpdateCurrentBuildingModel
{
    [Required(ErrorMessage = "Mã chung cư là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Mã chung cư không được vượt quá 50 ký tự.")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Tên chung cư là bắt buộc.")]
    [StringLength(200, ErrorMessage = "Tên chung cư không được vượt quá 200 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ là bắt buộc.")]
    [StringLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự.")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Múi giờ là bắt buộc.")]
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    [Range(1, 200, ErrorMessage = "Số tầng phải từ 1 đến 200.")]
    public int NumberOfFloors { get; set; } = 1;

    [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
    public string? Description { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;
}
