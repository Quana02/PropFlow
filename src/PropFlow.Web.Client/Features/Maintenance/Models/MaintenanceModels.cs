using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PropFlow.Web.Client.Features.Maintenance.Models;

public record Page<T>(IReadOnlyList<T> Items, int TotalCount, int PageIndex, int PageSize);
public enum MaintenanceScheduleStatus { ACTIVE, COMPLETED, CANCELLED }
public enum MaintenanceTaskStatus { OPEN, ASSIGNED, IN_PROGRESS, COMPLETED, CLOSED, CANCELLED }
public enum MaintenanceActivityType { CREATED, ASSIGNED, REASSIGNED, STATUS_CHANGED, PROGRESS_UPDATED, WORK_LOG_ADDED, RESULT_SUBMITTED, MANAGER_REVIEWED, SERVICE_REQUEST_LINKED, COMPLETED, CLOSED }
public enum MaintenanceResultStatus { SUBMITTED, APPROVED, REVISION_REQUIRED }
public enum MaintenanceAssignmentStatus { ASSIGNED, IN_PROGRESS, COMPLETED, REASSIGNED, CANCELLED }
public enum MaintenanceHistorySort { NEWEST, OLDEST }
public record AssetModel(Guid Id, string Code, string Name, string AssetType, string Status, string? LocationDescription, Guid? FacilityId = null);
public record ScheduleModel(Guid Id, string ScheduleCode, Guid? FacilityId, Guid? EquipmentId, string Title, string? Description, DateTimeOffset PlannedStartAt, DateTimeOffset? PlannedEndAt, MaintenanceScheduleStatus Status);

public sealed class ScheduleFilterModel
{
    public string? SearchKeyword { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid? EquipmentId { get; set; }
    public MaintenanceScheduleStatus? Status { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
public sealed class TaskFilterModel
{
    public string? SearchKeyword { get; set; }
    public MaintenanceTaskStatus? Status { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid? EquipmentId { get; set; }
    public Guid? AssignedStaffUserId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
public sealed class MyMaintenanceTaskFilterModel
{
    public string? SearchKeyword { get; set; }
    public MaintenanceTaskStatus? Status { get; set; }
    public string? PriorityCode { get; set; }
    public bool CompletedOnly { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}
public sealed class UpdateMaintenanceProgressModel { public string Content { get; set; } = string.Empty; }
public sealed class SubmitMaintenanceResultModel
{
    public string Summary { get; set; } = string.Empty;
    public string? WorkPerformed { get; set; }
    public string? IssueFound { get; set; }
    public string? PartsOrResourcesUsed { get; set; }
    public string? Recommendation { get; set; }
}
public record MaintenanceTaskActivityModel(Guid Id, MaintenanceActivityType ActivityType, string? Detail, DateTimeOffset CreatedAt, Guid? PerformedBy);

public sealed class CreateScheduleModel
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ScheduleCode { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập tên lịch bảo trì.")]
    [StringLength(200, ErrorMessage = "Tên lịch tối đa 200 ký tự.")]
    public string Title { get; set; } = string.Empty;
    [Required(ErrorMessage = "Vui lòng chọn đầy đủ ngày và giờ bắt đầu.")]
    public DateTimeOffset? PlannedStartAt { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn đầy đủ ngày và giờ kết thúc.")]
    public DateTimeOffset? PlannedEndAt { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid? EquipmentId { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập mô tả nhiệm vụ bảo trì.")]
    public string? Description { get; set; }
}

public sealed class UpdateScheduleModel
{
    [Required(ErrorMessage = "Vui lòng nhập tên lịch bảo trì.")]
    [StringLength(200, ErrorMessage = "Tên lịch tối đa 200 ký tự.")]
    public string Title { get; set; } = string.Empty;
    [Required(ErrorMessage = "Vui lòng chọn đầy đủ ngày và giờ bắt đầu.")]
    public DateTimeOffset? PlannedStartAt { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn đầy đủ ngày và giờ kết thúc.")]
    public DateTimeOffset? PlannedEndAt { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid? EquipmentId { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập mô tả nhiệm vụ bảo trì.")]
    public string? Description { get; set; }
}

public sealed record ScheduleStatusRequest(string Action);
public sealed class ExtendScheduleModel { [Range(1, 3650, ErrorMessage = "Vui lòng nhập thời lượng gia hạn hợp lệ.")] public int Amount { get; set; } = 1; [Required(ErrorMessage = "Vui lòng chọn đơn vị gia hạn.")] public string Unit { get; set; } = "hour"; }
public record MaintenanceResultAttachmentModel(Guid Id, string OriginalFileName, string ContentType, long SizeBytes);
public record MaintenanceResultModel(Guid Id, int AttemptNo, Guid SubmittedBy, string Summary, string? WorkPerformed, string? IssueFound, string? PartsOrResourcesUsed, string? Recommendation, MaintenanceResultStatus ResultStatus, Guid? ReviewedBy, string? ReviewNote, DateTimeOffset SubmittedAt, DateTimeOffset? ReviewedAt, IReadOnlyList<MaintenanceResultAttachmentModel>? Attachments = null);
public record RelatedServiceRequestModel(
    Guid Id,
    string RequestNumber,
    string Title,
    string Status,
    Guid? ApartmentUnitId,
    Guid? FacilityId,
    Guid? EquipmentId,
    string? PriorityCode,
    string? LocationDescription = null,
    string? ContextMatch = null,
    string? ContextWarning = null);
public record MaintenanceTaskModel(Guid Id, string TaskNumber, Guid? ScheduleId, Guid? FacilityId, Guid? EquipmentId, string Title, string? Description, string? PriorityCode, MaintenanceTaskStatus Status, DateTimeOffset? PlannedStartAt, DateTimeOffset? DueAt, Guid CreatedBy, DateTimeOffset CreatedAt, string? SourceScheduleCode = null, string? SourceScheduleTitle = null, Guid? AssignedStaffUserId = null, string? AssignedStaffDisplayName = null, string? AssignedStaffUsername = null, DateTimeOffset? AssignedAt = null, DateTimeOffset? LastActivityAt = null, MaintenanceActivityType? LastActivityType = null, DateTimeOffset? LastProgressAt = null, MaintenanceActivityType? LastProgressActivityType = null, string? LastProgressContent = null, bool IsOverdue = false, MaintenanceResultModel? LatestResult = null, IReadOnlyList<AssignableMaintenanceStaffModel>? CurrentAssignees = null, Guid? SourceServiceRequestId = null, RelatedServiceRequestModel? RelatedServiceRequest = null);
public sealed class CreateMaintenanceTaskModel
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaskNumber { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập tiêu đề công việc.")]
    [StringLength(200, ErrorMessage = "Tiêu đề công việc tối đa 200 ký tự.")] public string Title { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? ScheduleId { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập mô tả nhiệm vụ bảo trì.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn mức ưu tiên.")]
    [RegularExpression("^(HIGH|MEDIUM|LOW)$", ErrorMessage = "Vui lòng chọn mức ưu tiên.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PriorityCode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? PlannedStartAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? DueAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? FacilityId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? EquipmentId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? ServiceRequestId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool IsEmergency { get; set; }
}
public static class MaintenancePriority
{
    public static string DisplayName(string? priorityCode) => priorityCode?.Trim().ToUpperInvariant() switch
    {
        "HIGH" => "Cao",
        "MEDIUM" => "Trung bình",
        "LOW" => "Thấp",
        _ => "Chưa xác định"
    };
}
public sealed class LinkMaintenanceTaskServiceRequestModel { [Required(ErrorMessage = "Vui lòng chọn yêu cầu dịch vụ.")] public Guid ServiceRequestId { get; set; } }
public sealed class AssociateMaintenanceTaskAssetModel { public Guid? FacilityId { get; set; } public Guid? EquipmentId { get; set; } }
public record AssignableMaintenanceStaffModel(Guid UserId, string Username, string DisplayName);
public sealed class AssignMaintenanceTaskModel
{
    // Kept for backward-compatible clients; the API normalizes it into StaffUserIds.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? StaffUserId { get; set; }
    [MinLength(1, ErrorMessage = "Vui lòng chọn ít nhất một nhân viên xử lý.")]
    public List<Guid> StaffUserIds { get; set; } = [];
}
public sealed class ReviewMaintenanceResultModel { [Required] public string Decision { get; set; } = string.Empty; public string? ReviewNote { get; set; } }
public sealed class MaintenanceHistoryFilterModel
{
    public string? SearchKeyword { get; set; }
    public MaintenanceTaskStatus? Status { get; set; }
    public Guid? FacilityId { get; set; }
    public Guid? EquipmentId { get; set; }
    public Guid? StaffUserId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public MaintenanceHistorySort Sort { get; set; } = MaintenanceHistorySort.NEWEST;
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
public record HistoryParticipantModel(Guid UserId, string? Username, string? DisplayName);
public record MaintenanceHistoryItemModel(Guid TaskId, string TaskNumber, string Title, string? Description, string? PriorityCode, MaintenanceTaskStatus Status, Guid? FacilityId, Guid? EquipmentId, string? SourceScheduleCode, string? SourceScheduleTitle, DateTimeOffset? PlannedStartAt, DateTimeOffset? DueAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? ClosedAt, DateTimeOffset HistoryAt, IReadOnlyList<HistoryParticipantModel> Participants, string? ResultSummary, MaintenanceResultStatus? ResultStatus);
public record MaintenanceHistorySummaryModel(int Total, int Open, int Assigned, int InProgress, int Completed, int Closed, int Cancelled);
public record MaintenanceHistoryListModel(Page<MaintenanceHistoryItemModel> Page, MaintenanceHistorySummaryModel Summary);
public record MaintenanceAssignmentHistoryModel(Guid Id, Guid StaffUserId, string? StaffUsername, string? StaffDisplayName, MaintenanceAssignmentStatus Status, DateTimeOffset AssignedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? EndedAt, string? AssignmentNote);
public record MaintenanceHistoryTimelineEventModel(Guid? Id, DateTimeOffset OccurredAt, string Kind, MaintenanceActivityType? ActivityType = null, MaintenanceTaskStatus? FromStatus = null, MaintenanceTaskStatus? ToStatus = null, string? Detail = null, Guid? PerformedBy = null);
public record MaintenanceHistoryDetailModel(MaintenanceHistoryItemModel Task, IReadOnlyList<MaintenanceAssignmentHistoryModel> Assignments, IReadOnlyList<MaintenanceResultModel> Results, IReadOnlyList<MaintenanceHistoryTimelineEventModel> Timeline);
