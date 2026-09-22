using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;

namespace PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;

public class MaintenanceTask
{
    private readonly List<MaintenanceAssignment> _assignments = [];
    private readonly List<MaintenanceTaskActivity> _activities = [];
    private readonly List<MaintenanceResult> _results = [];

    private MaintenanceTask()
    {
    }

    public MaintenanceTask(
        string taskNumber,
        string title,
        Guid createdBy,
        DateTimeOffset now,
        Guid? scheduleId = null,
        Guid? sourceServiceRequestId = null,
        Guid? sourceComplaintId = null,
        Guid? facilityId = null,
        Guid? equipmentId = null,
        string? description = null,
        string? priorityCode = null,
        DateTimeOffset? plannedStartAt = null,
        DateTimeOffset? dueAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ThrowIfEmpty(createdBy, nameof(createdBy));
        EnsureDueAfterPlannedStart(plannedStartAt, dueAt);

        Id = Guid.NewGuid();
        TaskNumber = taskNumber.Trim();
        ScheduleId = scheduleId;
        SourceServiceRequestId = sourceServiceRequestId;
        SourceComplaintId = sourceComplaintId;
        FacilityId = facilityId;
        EquipmentId = equipmentId;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        PriorityCode = string.IsNullOrWhiteSpace(priorityCode) ? null : priorityCode.Trim().ToUpperInvariant();
        Status = MaintenanceTaskStatus.OPEN;
        PlannedStartAt = plannedStartAt;
        DueAt = dueAt;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string TaskNumber { get; private set; } = null!;
    public Guid? ScheduleId { get; private set; }
    public Guid? SourceServiceRequestId { get; private set; }
    public Guid? SourceComplaintId { get; private set; }
    public Guid? FacilityId { get; private set; }
    public Guid? EquipmentId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? PriorityCode { get; private set; }
    public MaintenanceTaskStatus Status { get; private set; }
    public DateTimeOffset? PlannedStartAt { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public MaintenanceSchedule? Schedule { get; private set; }
    public IReadOnlyCollection<MaintenanceAssignment> Assignments => _assignments.AsReadOnly();
    public IReadOnlyCollection<MaintenanceTaskActivity> Activities => _activities.AsReadOnly();
    public IReadOnlyCollection<MaintenanceResult> Results => _results.AsReadOnly();

    public void UpdateDetails(
        string title,
        DateTimeOffset now,
        string? description = null,
        string? priorityCode = null,
        DateTimeOffset? plannedStartAt = null,
        DateTimeOffset? dueAt = null)
    {
        EnsureNotTerminal();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        EnsureDueAfterPlannedStart(plannedStartAt, dueAt);

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        PriorityCode = string.IsNullOrWhiteSpace(priorityCode) ? null : priorityCode.Trim().ToUpperInvariant();
        PlannedStartAt = plannedStartAt;
        DueAt = dueAt;
        UpdatedAt = now;
    }

    public void MarkAssigned(DateTimeOffset now)
    {
        EnsureNotTerminal();
        if (Status is not (MaintenanceTaskStatus.OPEN or MaintenanceTaskStatus.ASSIGNED or MaintenanceTaskStatus.IN_PROGRESS))
            throw new InvalidOperationException("Only open or active maintenance tasks can be assigned.");

        Status = MaintenanceTaskStatus.ASSIGNED;
        UpdatedAt = now;
    }

    public void Start(DateTimeOffset now)
    {
        EnsureNotTerminal();
        if (Status is not (MaintenanceTaskStatus.ASSIGNED or MaintenanceTaskStatus.IN_PROGRESS))
            throw new InvalidOperationException("Only assigned maintenance tasks can be started.");

        Status = MaintenanceTaskStatus.IN_PROGRESS;
        StartedAt ??= now;
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureNotTerminal();
        if (Status != MaintenanceTaskStatus.IN_PROGRESS)
            throw new InvalidOperationException("Only in-progress maintenance tasks can be completed.");
        if (StartedAt is not null && now < StartedAt)
            throw new ArgumentException("Completed time cannot be before started time.", nameof(now));

        Status = MaintenanceTaskStatus.COMPLETED;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Close(Guid closedBy, DateTimeOffset now)
    {
        EnsureNotTerminal();
        ThrowIfEmpty(closedBy, nameof(closedBy));
        if (Status != MaintenanceTaskStatus.COMPLETED)
            throw new InvalidOperationException("Only completed maintenance tasks can be closed.");
        if (CompletedAt is not null && now < CompletedAt)
            throw new ArgumentException("Closed time cannot be before completed time.", nameof(now));

        Status = MaintenanceTaskStatus.CLOSED;
        ClosedBy = closedBy;
        ClosedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureNotTerminal();

        Status = MaintenanceTaskStatus.CANCELLED;
        UpdatedAt = now;
    }

    private void EnsureNotTerminal()
    {
        if (Status is MaintenanceTaskStatus.CLOSED or MaintenanceTaskStatus.CANCELLED)
            throw new InvalidOperationException("Closed or cancelled maintenance tasks cannot be modified.");
    }

    private static void EnsureDueAfterPlannedStart(DateTimeOffset? plannedStartAt, DateTimeOffset? dueAt)
    {
        if (plannedStartAt is not null && dueAt is not null && dueAt < plannedStartAt)
            throw new ArgumentException("Due time must be greater than or equal to planned start.", nameof(dueAt));
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
