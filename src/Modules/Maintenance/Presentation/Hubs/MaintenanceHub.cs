using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Maintenance.Contracts;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

[Authorize]
public class MaintenanceHub(MaintenanceDbContext db, IAuthorizationService authorization) : Hub
{
    private const string ManagerRole = "MANAGER";
    private const string StaffRole = "STAFF";

    public static string TaskGroup(Guid taskId) => $"maintenance-task:{taskId}";
    public static string UserUpdatesGroup(Guid userId) => $"maintenance-user:{userId}";
    public const string AssetStatusGroup = "property-assets:status";

    public async Task JoinTask(Guid taskId)
    {
        await EnsurePolicyAsync(MaintenanceAuthorizationPolicies.View, "Bạn không có quyền theo dõi công việc bảo trì.");
        await EnsureCanObserveTaskAsync(taskId);
        await Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskId));
    }

    public Task LeaveTask(Guid taskId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskId));

    public async Task JoinMaintenanceUpdates()
    {
        await EnsurePolicyAsync(MaintenanceAuthorizationPolicies.View, "Bạn không có quyền nhận cập nhật công việc bảo trì.");
        if (!Guid.TryParse(Context.UserIdentifier, out var userId))
            throw new HubException("Không xác định được người dùng hiện tại.");

        await Groups.AddToGroupAsync(Context.ConnectionId, UserUpdatesGroup(userId));
    }

    public async Task JoinAssetStatus()
    {
        await EnsurePolicyAsync(PropertyAssetsAuthorizationPolicies.View, "Bạn không có quyền theo dõi trạng thái tài sản.");
        await Groups.AddToGroupAsync(Context.ConnectionId, AssetStatusGroup);
    }

    public Task LeaveAssetStatus() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, AssetStatusGroup);

    private async Task EnsurePolicyAsync(string policy, string message)
    {
        if (Context.User is null)
            throw new HubException(message);

        var result = await authorization.AuthorizeAsync(Context.User, policy);
        if (!result.Succeeded)
            throw new HubException(message);
    }

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
