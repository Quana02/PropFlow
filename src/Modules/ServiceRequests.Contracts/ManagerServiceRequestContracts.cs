namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record ManagerServiceRequestListQuery(
    string? Search = null,
    string? Status = null,
    string? Priority = null,
    bool UnassignedOnly = false,
    int Page = 1,
    int PageSize = 20,
    int? SubmittedWithinDays = null);

public sealed record ManagerServiceRequestSummary(
    int Total,
    int Submitted,
    int InProgress,
    int Completed);

public sealed record ManagerServiceRequestListItem(
    Guid Id,
    string RequestNumber,
    string Title,
    string? CategoryCode,
    string? CategoryName,
    Guid ResidentId,
    string? ResidentCode,
    string? ResidentName,
    Guid? ApartmentUnitId,
    string? ApartmentUnitNumber,
    int? FloorNumber,
    string Status,
    string? PriorityCode,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    Guid? AssignedStaffUserId,
    string? AssignedStaffUsername,
    string? AssignedStaffDisplayName);

public sealed record ManagerServiceRequestListResponse(
    IReadOnlyList<ManagerServiceRequestListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    ManagerServiceRequestSummary Summary);

public sealed record ManagerServiceRequestActivityItem(
    Guid Id,
    string ActivityType,
    string? FromStatus,
    string? ToStatus,
    string? Title,
    string? Detail,
    Guid? PerformedBy,
    DateTimeOffset CreatedAt);

public sealed record ManagerServiceRequestAiInsight(
    string? ClassificationStatus,
    Guid? PredictedCategoryId,
    decimal? ClassificationConfidence,
    string? ClassificationReasoning,
    string? RecommendationStatus,
    string? SuggestedPriorityCode,
    bool? MaintenanceRecommended,
    string? RecommendedAction,
    string? RecommendedResourceSummary,
    string? RecommendationReasoning);

public sealed record ManagerServiceRequestAsset(
    Guid Id,
    string Code,
    string Name,
    string AssetType,
    string Status,
    string? LocationDescription);

public sealed record ManagerServiceRequestDetailResponse(
    Guid Id,
    string RequestNumber,
    string Title,
    string Description,
    string? CategoryCode,
    string? CategoryName,
    Guid ResidentId,
    string? ResidentCode,
    string? ResidentName,
    Guid ResidentApartmentId,
    Guid? ApartmentUnitId,
    string? ApartmentUnitNumber,
    int? FloorNumber,
    string Status,
    string? PriorityCode,
    string? ServiceAreaCode,
    DateOnly? PreferredDate,
    string? PreferredTimeCode,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    Guid? AssignedStaffUserId,
    ManagerServiceRequestAsset? Facility,
    ManagerServiceRequestAsset? Equipment,
    ManagerServiceRequestAiInsight? AiInsight,
    IReadOnlyList<ManagerServiceRequestActivityItem> Activities);

public sealed record ReviewManagerServiceRequestRequest(string? Note = null);

public sealed record AssignManagerServiceRequestRequest(
    Guid StaffUserId,
    string? Note = null);

public sealed record ManagerServiceRequestStaffOption(
    Guid UserId,
    string Username,
    string DisplayName);

public sealed record ManagerServiceRequestAssignmentOptionsResponse(
    IReadOnlyList<ManagerServiceRequestStaffOption> Staff);

public sealed record ConfirmManagerServiceRequestPropertyContextRequest(
    Guid? ApartmentUnitId,
    Guid? FacilityId,
    Guid? EquipmentId,
    string? ServiceAreaCode = null,
    string? Note = null);

public sealed record ManagerPropertyContextOption(
    Guid Id,
    string Code,
    string Name,
    string? SecondaryText,
    string? Status,
    Guid? ParentId = null);

public sealed record ManagerPropertyContextOptionsResponse(
    IReadOnlyList<ManagerPropertyContextOption> Apartments,
    IReadOnlyList<ManagerPropertyContextOption> Facilities,
    IReadOnlyList<ManagerPropertyContextOption> Equipment);

public interface IManagerServiceRequestInsightSource
{
    Task<ManagerServiceRequestAiInsight?> GetAsync(Guid serviceRequestId, CancellationToken ct);
}
