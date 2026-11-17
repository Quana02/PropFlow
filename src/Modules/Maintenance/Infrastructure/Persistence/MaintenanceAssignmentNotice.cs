namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence;

public sealed record MaintenanceAssignmentNotice(Guid ManagerUserId, Guid StaffUserId, Guid? AssignmentId = null)
{
    public const string EventName = "MaintenanceTaskAssigned";
}
