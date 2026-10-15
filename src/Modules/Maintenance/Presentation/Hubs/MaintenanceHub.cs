using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Maintenance.Contracts;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

[Authorize(Policy = MaintenanceAuthorizationPolicies.View)]
public class MaintenanceHub(MaintenanceDbContext db) : Hub
{
    private const string ManagerRole = "MANAGER";
    private const string StaffRole = "STAFF";

    public static string TaskGroup(Guid taskId) => $"maintenance-task:{taskId}";

    public async Task JoinTask(Guid taskId)
    {
        await EnsureCanObserveTaskAsync(taskId);
        await Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskId));
    }

    public Task LeaveTask(Guid taskId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskId));

    private async Task EnsureCanObserveTaskAsync(Guid taskId)
    {
        if (Context.User?.IsInRole(ManagerRole) == true)
            return;

        if (Context.User?.IsInRole(StaffRole) != true ||
            !Guid.TryParse(Context.UserIdentifier, out var staffUserId))
            throw new HubException("Bạn không có quyền theo dõi công việc này.");

        var isAssigned = await db.MaintenanceAssignments.AsNoTracking()
            .AnyAsync(assignment => assignment.MaintenanceTaskId == taskId && assignment.StaffUserId == staffUserId);
        if (!isAssigned)
            throw new HubException("Bạn không có quyền theo dõi công việc này.");
    }
}
