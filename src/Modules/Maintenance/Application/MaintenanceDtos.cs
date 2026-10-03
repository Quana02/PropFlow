using System.ComponentModel.DataAnnotations;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;

namespace PropFlow.Modules.Maintenance.Application;

public record Page<T>(IReadOnlyList<T> Items, int TotalCount, int PageIndex, int PageSize);
public record ScheduleDto(Guid Id, string ScheduleCode, Guid? FacilityId, Guid? EquipmentId, string Title, string? Description, DateTimeOffset PlannedStartAt, DateTimeOffset? PlannedEndAt, MaintenanceScheduleStatus Status);

public record ScheduleQuery(string? SearchKeyword = null, Guid? FacilityId = null, Guid? EquipmentId = null, MaintenanceScheduleStatus? Status = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int PageIndex = 1, int PageSize = 20);
public record CreateScheduleCommand([Required, StringLength(30)] string ScheduleCode, [Required, StringLength(200)] string Title, DateTimeOffset PlannedStartAt, DateTimeOffset? PlannedEndAt = null, [Required] Guid? FacilityId = null, Guid? EquipmentId = null, string? Description = null);
public record UpdateScheduleCommand([Required, StringLength(200)] string Title, DateTimeOffset PlannedStartAt, DateTimeOffset? PlannedEndAt = null, [Required] Guid? FacilityId = null, Guid? EquipmentId = null, string? Description = null);
public record ScheduleStatusCommand([Required] string Action);
public record ExtendScheduleCommand([Range(1, 3650)] int Amount, [Required] string Unit);
public record MaintenanceResultDto(Guid Id, int AttemptNo, Guid SubmittedBy, string Summary, string? WorkPerformed, string? IssueFound, string? PartsOrResourcesUsed, string? Recommendation, MaintenanceResultStatus ResultStatus, Guid? ReviewedBy, string? ReviewNote, DateTimeOffset SubmittedAt, DateTimeOffset? ReviewedAt);
public record MaintenanceTaskDto(Guid Id, string TaskNumber, Guid? ScheduleId, Guid? FacilityId, Guid? EquipmentId, string Title, string? Description, string? PriorityCode, MaintenanceTaskStatus Status, DateTimeOffset? PlannedStartAt, DateTimeOffset? DueAt, Guid CreatedBy, DateTimeOffset CreatedAt, string? SourceScheduleCode = null, string? SourceScheduleTitle = null, Guid? AssignedStaffUserId = null, string? AssignedStaffDisplayName = null, string? AssignedStaffUsername = null, DateTimeOffset? AssignedAt = null, DateTimeOffset? LastActivityAt = null, MaintenanceActivityType? LastActivityType = null, DateTimeOffset? LastProgressAt = null, MaintenanceActivityType? LastProgressActivityType = null, bool IsOverdue = false, MaintenanceResultDto? LatestResult = null);
public record TaskQuery(string? SearchKeyword = null, MaintenanceTaskStatus? Status = null, Guid? FacilityId = null, Guid? EquipmentId = null, Guid? AssignedStaffUserId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int PageIndex = 1, int PageSize = 20);
public record CreateMaintenanceTaskCommand([Required, StringLength(30)] string TaskNumber, [Required, StringLength(200)] string Title, Guid? ScheduleId = null, string? Description = null, string? PriorityCode = null, DateTimeOffset? PlannedStartAt = null, DateTimeOffset? DueAt = null, Guid? FacilityId = null, Guid? EquipmentId = null);
public record AssociateMaintenanceTaskAssetCommand(Guid? FacilityId, Guid? EquipmentId);
public record AssignMaintenanceTaskCommand(Guid StaffUserId);
public record AssignableMaintenanceStaffDto(Guid UserId, string Username, string DisplayName);
public record ReviewMaintenanceResultCommand([Required] string Decision, string? ReviewNote = null);
public enum MaintenanceHistorySort { NEWEST, OLDEST }
public record MaintenanceHistoryQuery(string? SearchKeyword = null, MaintenanceTaskStatus? Status = null, Guid? FacilityId = null, Guid? EquipmentId = null, Guid? StaffUserId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, MaintenanceHistorySort Sort = MaintenanceHistorySort.NEWEST, int PageIndex = 1, int PageSize = 20);
public record HistoryParticipantDto(Guid UserId, string? Username, string? DisplayName);
public record MaintenanceHistoryItemDto(Guid TaskId, string TaskNumber, string Title, string? Description, string? PriorityCode, MaintenanceTaskStatus Status, Guid? FacilityId, Guid? EquipmentId, string? SourceScheduleCode, string? SourceScheduleTitle, DateTimeOffset? PlannedStartAt, DateTimeOffset? DueAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? ClosedAt, DateTimeOffset HistoryAt, IReadOnlyList<HistoryParticipantDto> Participants, string? ResultSummary, MaintenanceResultStatus? ResultStatus);
public record MaintenanceHistorySummaryDto(int Total, int Open, int Assigned, int InProgress, int Completed, int Closed, int Cancelled);
public record MaintenanceHistoryListDto(Page<MaintenanceHistoryItemDto> Page, MaintenanceHistorySummaryDto Summary);
public record MaintenanceAssignmentHistoryDto(Guid Id, Guid StaffUserId, string? StaffUsername, string? StaffDisplayName, AssignmentStatus Status, DateTimeOffset AssignedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? EndedAt, string? AssignmentNote);
public record MaintenanceHistoryTimelineEventDto(Guid? Id, DateTimeOffset OccurredAt, string Kind, MaintenanceActivityType? ActivityType = null, MaintenanceTaskStatus? FromStatus = null, MaintenanceTaskStatus? ToStatus = null, string? Detail = null, Guid? PerformedBy = null);
public record MaintenanceHistoryDetailDto(MaintenanceHistoryItemDto Task, IReadOnlyList<MaintenanceAssignmentHistoryDto> Assignments, IReadOnlyList<MaintenanceResultDto> Results, IReadOnlyList<MaintenanceHistoryTimelineEventDto> Timeline);
