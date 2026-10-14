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
    public async Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientStaffUserIds, CancellationToken ct = default)
    {
        var recipients = recipientStaffUserIds.Where(userId => userId != Guid.Empty).Distinct()
            .Select(userId => userId.ToString()).ToArray();

        // A group reaches the Manager detail and any Staff detail currently open for this task.
        // Direct recipient delivery also refreshes a Staff's task list after assignment changes.
        var groupNotification = hubContext.Clients.Group(MaintenanceHub.TaskGroup(taskId))
            .SendAsync("MaintenanceTaskUpdated", taskId, cancellationToken: ct);
        var recipientNotification = recipients.Length == 0
            ? Task.CompletedTask
            : hubContext.Clients.Users(recipients).SendAsync("MaintenanceTaskUpdated", taskId, cancellationToken: ct);

        await Task.WhenAll(groupNotification, recipientNotification);
    }
}
