using Microsoft.AspNetCore.SignalR;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

public sealed class MaintenanceUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst("sub")?.Value;
}
