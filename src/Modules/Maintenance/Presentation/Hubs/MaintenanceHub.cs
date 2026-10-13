using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Maintenance.Contracts;

namespace PropFlow.Modules.Maintenance.Presentation.Hubs;

[Authorize(Policy = MaintenanceAuthorizationPolicies.View)]
public class MaintenanceHub : Hub
{
}
