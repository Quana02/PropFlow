namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record CreateResidentServiceRequestRequest(
    string CategoryCode,
    string Title,
    string Description,
    string ServiceAreaCode,
    string PriorityCode,
    DateOnly? PreferredDate,
    string? PreferredTimeCode);

public sealed record ResidentServiceRequestCreatedResponse(
    Guid Id,
    string RequestNumber,
    string Status,
    DateTimeOffset SubmittedAt);

public sealed record ResidentServiceRequestListQuery(
    string? Search = null,
    string? Status = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null);

public sealed record ResidentServiceRequestListItem(
    Guid Id,
    string RequestNumber,
    string Title,
    string? CategoryCode,
    string Status,
    string? PriorityCode,
    string? ServiceAreaCode,
    DateOnly? PreferredDate,
    string? PreferredTimeCode,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    int? Rating,
    string? FeedbackComment,
    DateTimeOffset? FeedbackSubmittedAt);

public sealed record ResidentServiceRequestActivityItem(
    Guid Id,
    string ActivityType,
    string? FromStatus,
    string? ToStatus,
    string Title,
    string Description,
    DateTimeOffset CreatedAt);

public sealed record ResidentServiceRequestDetailResponse(
    Guid Id,
    string RequestNumber,
    string Title,
    string Description,
    string? CategoryCode,
    string? CategoryName,
    string Status,
    string? PriorityCode,
    string? ServiceAreaCode,
    DateOnly? PreferredDate,
    string? PreferredTimeCode,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    int? Rating,
    string? FeedbackComment,
    DateTimeOffset? FeedbackSubmittedAt,
    IReadOnlyList<ResidentServiceRequestActivityItem> Activities);

public sealed record RateResidentServiceRequestRequest(int Rating, string Comment);

public sealed record ResidentServiceRequestFeedbackResponse(
    Guid ServiceRequestId,
    int Rating,
    string Comment,
    DateTimeOffset SubmittedAt);
