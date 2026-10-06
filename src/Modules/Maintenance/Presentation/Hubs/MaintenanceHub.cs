using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

public class MaintenanceHub : Hub
{
    // Clients can optionally join specific task groups if needed
    public async Task JoinTaskGroup(string taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Task_{taskId}");
    }

    public async Task LeaveTaskGroup(string taskId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Task_{taskId}");
    }
}
