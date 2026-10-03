namespace PropFlow.Web.Client.Features.Maintenance.Models;

// Presentation-only lifecycle mapping. It does not persist a progress step or
// infer a percentage; future Staff activities can advance the displayed step.
public sealed record MaintenanceProgressLifecycle(int CurrentStep, bool IsCancelled);

public static class MaintenanceProgressLifecycleMapper
{
    public static MaintenanceProgressLifecycle Map(MaintenanceTaskModel task) => task.Status switch
    {
        MaintenanceTaskStatus.ASSIGNED => new(1, false),
        MaintenanceTaskStatus.IN_PROGRESS when task.LastProgressActivityType is MaintenanceActivityType.PROGRESS_UPDATED or MaintenanceActivityType.WORK_LOG_ADDED => new(3, false),
        MaintenanceTaskStatus.IN_PROGRESS => new(2, false),
        MaintenanceTaskStatus.COMPLETED => new(4, false),
        MaintenanceTaskStatus.CLOSED => new(5, false),
        MaintenanceTaskStatus.CANCELLED => new(0, true),
        _ => new(0, false)
    };
}
