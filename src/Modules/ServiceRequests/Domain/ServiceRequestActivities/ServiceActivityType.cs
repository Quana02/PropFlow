namespace PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;

public enum ServiceActivityType
{
    CREATED,
    STATUS_CHANGED,
    ASSIGNED,
    REASSIGNED,
    PROGRESS_UPDATED,
    WORK_RESULT_ADDED,
    REWORK_REQUESTED,
    MANAGER_REVIEWED,
    PROPERTY_CONTEXT_ASSOCIATED,
    RESOLVED,
    CLOSED
}
