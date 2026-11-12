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
    [Required(ErrorMessage = "Vui lòng mô tả vấn đề cần hỗ trợ.")]
    [StringLength(3500, MinimumLength = 10, ErrorMessage = "Mô tả cần từ 10 đến 3500 ký tự.")]
    public string Description { get; set; } = string.Empty;

    public DateOnly? PreferredDate { get; set; }

    public string? PreferredTimeCode { get; set; }
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
