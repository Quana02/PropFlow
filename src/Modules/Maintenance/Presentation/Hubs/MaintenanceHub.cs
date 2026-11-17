using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Maintenance.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

[Authorize(Policy = MaintenanceAuthorizationPolicies.View)]
public class MaintenanceHub : Hub
{
    public static string TaskGroup(Guid taskId) => $"maintenance-task:{taskId}";
    public static string UserUpdatesGroup(Guid userId) => $"maintenance-user:{userId}";
    public const string AssetStatusGroup = "property-assets:status";

    // Staff receive task events through the recipient list resolved by the assignment workflow.
    [Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)]
    public Task JoinTask(Guid taskId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskId));

    [Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)]
    public Task LeaveTask(Guid taskId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskId));

    public async Task JoinMaintenanceUpdates()
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId))
            throw new HubException("Không xác định được người dùng hiện tại.");

        await Groups.AddToGroupAsync(Context.ConnectionId, UserUpdatesGroup(userId));
    }

    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.View)]
    public Task JoinAssetStatus() =>
        Groups.AddToGroupAsync(Context.ConnectionId, AssetStatusGroup);

    public Task LeaveAssetStatus() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, AssetStatusGroup);
}
