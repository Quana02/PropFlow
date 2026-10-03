using System.ComponentModel.DataAnnotations;

namespace PropFlow.Web.Client.Features.Resident.Portal.Models;

public static class ServiceRequestStatuses
{
    public const string Submitted = "SUBMITTED";
    public const string UnderReview = "UNDER_REVIEW";
    public const string Assigned = "ASSIGNED";
    public const string InProgress = "IN_PROGRESS";
    public const string Resolved = "RESOLVED";
    public const string Closed = "CLOSED";
    public const string Cancelled = "CANCELLED";
}

public sealed class SubmitServiceRequestForm
{
    [Required(ErrorMessage = "Vui lòng chọn loại dịch vụ.")]
    public string CategoryCode { get; set; } = "REPAIR";

    [Required(ErrorMessage = "Vui lòng chọn phạm vi tiếp nhận.")]
    [RegularExpression("PRIVATE|PUBLIC", ErrorMessage = "Phạm vi tiếp nhận không hợp lệ.")]
    public string ScopeCode { get; set; } = "PRIVATE";

    [Required(ErrorMessage = "Vui lòng chọn loại yêu cầu.")]
    public string RequestTypeCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tiêu đề yêu cầu.")]
    [StringLength(200, ErrorMessage = "Tiêu đề không được vượt quá 200 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng mô tả vấn đề cần hỗ trợ.")]
    [StringLength(3500, MinimumLength = 10, ErrorMessage = "Mô tả cần từ 10 đến 3500 ký tự.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn khu vực hỗ trợ.")]
    public string ServiceAreaCode { get; set; } = "MASTER_BATHROOM";

    [StringLength(300, ErrorMessage = "Vị trí chi tiết không được vượt quá 300 ký tự.")]
    public string? LocationDetail { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn mức độ ảnh hưởng.")]
    [RegularExpression("STANDARD|SERIOUS|EMERGENCY", ErrorMessage = "Mức độ ảnh hưởng không hợp lệ.")]
    public string ImpactCode { get; set; } = "STANDARD";

    [Required]
    [RegularExpression("NORMAL|URGENT", ErrorMessage = "Mức độ ưu tiên không hợp lệ.")]
    public string PriorityCode { get; set; } = "NORMAL";

    public DateOnly? PreferredDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

    public string? PreferredTimeCode { get; set; } = "MORNING";
}

public sealed record ServiceRequestListItemViewModel(
    Guid Id,
    string RequestNumber,
    string Title,
    string Status,
    DateTimeOffset SubmittedAt,
    string? CategoryName);

public sealed record ServiceRequestActivityViewModel(
    Guid Id,
    string ActivityType,
    string? FromStatus,
    string? ToStatus,
    string? Title,
    string? Detail,
    string? WorkResult,
    DateTimeOffset CreatedAt);

public sealed record ServiceRequestDetailViewModel(
    Guid Id,
    string RequestNumber,
    string Title,
    string Description,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    string? CategoryName,
    string? FinalPriorityCode,
    IReadOnlyList<ServiceRequestActivityViewModel> Activities);
