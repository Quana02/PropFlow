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
    public Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientStaffUserIds, CancellationToken ct = default)
    {
        var recipients = recipientStaffUserIds.Where(userId => userId != Guid.Empty).Distinct()
            .Select(userId => userId.ToString()).ToArray();
        return recipients.Length == 0
            ? Task.CompletedTask
            : hubContext.Clients.Users(recipients).SendAsync("TaskUpdated", taskId, cancellationToken: ct);
    }
}
