using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Maintenance.Application;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

public class MaintenanceNotifier(IHubContext<MaintenanceHub> hubContext) : IMaintenanceNotifier
{
    public async Task NotifyTaskUpdatedAsync(Guid taskId, CancellationToken ct = default)
    {
        await hubContext.Clients.All.SendAsync("TaskUpdated", taskId, cancellationToken: ct);
    }
}
