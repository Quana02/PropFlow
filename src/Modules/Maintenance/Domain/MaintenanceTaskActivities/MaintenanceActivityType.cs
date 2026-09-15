namespace PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;

public enum MaintenanceActivityType
{
    CREATED,
    ASSIGNED,
    REASSIGNED,
    STATUS_CHANGED,
    PROGRESS_UPDATED,
    WORK_LOG_ADDED,
    RESULT_SUBMITTED,
    MANAGER_REVIEWED,
    COMPLETED,
    CLOSED
}
