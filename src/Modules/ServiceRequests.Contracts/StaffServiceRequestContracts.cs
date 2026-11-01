namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record StaffServiceRequestListQuery(
    string? Search = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20);

public sealed record StaffServiceRequestSummary(
    int Total,
    int Assigned,
    int InProgress,
    int Resolved);

public sealed record StaffServiceRequestListItem(
    Guid Id,
    string RequestNumber,
    string Title,
    string Description,
    string? ApartmentUnitNumber,
    int? FloorNumber,
    string? FacilityName,
    string? EquipmentName,
    string Status,
    string? PriorityCode,
    DateTimeOffset AssignedAt,
    DateTimeOffset UpdatedAt);

public sealed record StaffServiceRequestListResponse(
    IReadOnlyList<StaffServiceRequestListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    StaffServiceRequestSummary Summary);

public sealed record StaffServiceRequestActivityItem(
    Guid Id,
    string ActivityType,
    string? Title,
    string? Detail,
    string? WorkResult,
    Guid? PerformedBy,
    DateTimeOffset CreatedAt);

public sealed record StaffServiceRequestEvidenceItem(
    Guid Id,
    Guid ActivityId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string DownloadUrl);

public sealed record StaffServiceRequestDetailResponse(
    Guid Id,
    string RequestNumber,
    string Title,
    string Description,
    string? CategoryName,
    string? ApartmentUnitNumber,
    int? FloorNumber,
    string? FacilityName,
    string? EquipmentName,
    string Status,
    string? PriorityCode,
    string? ServiceAreaCode,
    DateOnly? PreferredDate,
    string? PreferredTimeCode,
    DateTimeOffset AssignedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<StaffServiceRequestActivityItem> Activities,
    IReadOnlyList<StaffServiceRequestEvidenceItem> Evidence,
    decimal BaseServiceCost = 0,
    decimal ProposedAdditionalCost = 0,
    string? AdditionalCostNote = null);

public sealed record UpdateStaffServiceRequestProgressRequest(string Detail);
public sealed record CompleteStaffServiceRequestWorkRequest(
    string WorkResult,
    decimal AdditionalCost = 0,
    string? AdditionalCostNote = null);
