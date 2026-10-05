namespace PropFlow.Web.Client.Features.Maintenance.Models;

// Presentation-only lifecycle mapping. It does not persist a progress step or
// infer a percentage; status transitions are the sole source of tracker state.
public sealed record MaintenanceProgressLifecycle(int CurrentStep, int CompletedThroughStep, bool IsCancelled)
{
    public bool IsStepCompleted(int step) => !IsCancelled && step <= CompletedThroughStep;
    public bool IsStepCurrent(int step) => !IsCancelled && step == CurrentStep;
}

public static class MaintenanceProgressLifecycleMapper
{
    public static MaintenanceProgressLifecycle Map(MaintenanceTaskModel task) => task.Status switch
    {
        MaintenanceTaskStatus.OPEN => new(1, 0, false),
        MaintenanceTaskStatus.ASSIGNED => new(2, 1, false),
        MaintenanceTaskStatus.IN_PROGRESS => new(3, 2, false),
        MaintenanceTaskStatus.COMPLETED => new(4, 3, false),
        MaintenanceTaskStatus.CLOSED => new(0, 5, false),
        MaintenanceTaskStatus.CANCELLED => new(0, 0, true),
        _ => new(1, 0, false)
    };
}
