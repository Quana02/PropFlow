using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PropFlow.Modules.RemoteOnlineSign.Presentation;

// Delivery is addressed by the server to authenticated user IDs. Clients do
// not choose a contract group or supply an ID to subscribe to.
[Authorize(Roles = "ADMIN,RESIDENT")]
public sealed class RemoteOnlineSignHub : Hub
{
}
