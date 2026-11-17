namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence;

public static class MaintenanceTaskLock
{
    public static string Scope(Guid taskId) => $"maintenance:task:{taskId:N}";
}
