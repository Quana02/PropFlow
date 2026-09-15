using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;

namespace PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;

public class MaintenanceTaskActivity
{
    private MaintenanceTaskActivity()
    {
    }

    public MaintenanceTaskActivity(
        Guid maintenanceTaskId,
        MaintenanceActivityType activityType,
        DateTimeOffset now,
        Guid? assignmentId = null,
        MaintenanceTaskStatus? fromStatus = null,
        MaintenanceTaskStatus? toStatus = null,
        string? detail = null,
        Guid? performedBy = null)
    {
        ThrowIfEmpty(maintenanceTaskId, nameof(maintenanceTaskId));

        Id = Guid.NewGuid();
        MaintenanceTaskId = maintenanceTaskId;
        AssignmentId = assignmentId;
        ActivityType = activityType;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        PerformedBy = performedBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid MaintenanceTaskId { get; private set; }
    public Guid? AssignmentId { get; private set; }
    public MaintenanceActivityType ActivityType { get; private set; }
    public MaintenanceTaskStatus? FromStatus { get; private set; }
    public MaintenanceTaskStatus? ToStatus { get; private set; }
    public string? Detail { get; private set; }
    public Guid? PerformedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public MaintenanceTask? MaintenanceTask { get; private set; }
    public MaintenanceAssignment? Assignment { get; private set; }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
