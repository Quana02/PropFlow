namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence;

public sealed record MaintenanceAssignmentNotice(Guid ManagerUserId, Guid StaffUserId)
{
    public const string EventName = "MaintenanceTaskAssigned";
}
