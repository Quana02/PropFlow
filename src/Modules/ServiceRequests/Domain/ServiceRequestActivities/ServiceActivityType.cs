namespace PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;

public enum ServiceActivityType
{
    CREATED,
    STATUS_CHANGED,
    ASSIGNED,
    REASSIGNED,
    PROGRESS_UPDATED,
    WORK_RESULT_ADDED,
    MANAGER_REVIEWED,
    RESOLVED,
    CLOSED
}
