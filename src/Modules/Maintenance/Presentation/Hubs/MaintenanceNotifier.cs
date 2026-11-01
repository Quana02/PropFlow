using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Maintenance.Application;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

public class MaintenanceNotifier(IHubContext<MaintenanceHub> hubContext) : IMaintenanceNotifier
{
    public Task NotifyAssetStatusChangedAsync(CancellationToken ct = default) =>
        hubContext.Clients.Group(MaintenanceHub.AssetStatusGroup)
            .SendAsync("AssetStatusChanged", cancellationToken: ct);

    public async Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientStaffUserIds, CancellationToken ct = default)
    {
        var recipients = recipientStaffUserIds.Where(userId => userId != Guid.Empty).Distinct()
            .Select(userId => userId.ToString()).ToArray();

        // A task group reaches its open detail. Per-user groups are opt-in and policy-protected by the hub,
        // so a maintenance event is never sent to an arbitrary authenticated connection.
        var groupNotification = hubContext.Clients.Group(MaintenanceHub.TaskGroup(taskId))
            .SendAsync("MaintenanceTaskUpdated", taskId, cancellationToken: ct);
        var recipientNotification = recipients.Length == 0
            ? Task.CompletedTask
            : hubContext.Clients.Groups(recipients.Select(id => MaintenanceHub.UserUpdatesGroup(Guid.Parse(id))))
                .SendAsync("MaintenanceTaskUpdated", taskId, cancellationToken: ct);

        await Task.WhenAll(groupNotification, recipientNotification);
    }
}
