using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PropFlow.Modules.ServiceRequests.Presentation.Hubs;

[Authorize(Roles = "MANAGER,STAFF,RESIDENT")]
public sealed class ServiceRequestHub : Hub
{
    public const string UpdatedEvent = "ServiceRequestUpdated";
    public const string ManagerGroup = "service-requests:managers";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("MANAGER") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, ManagerGroup);

        await base.OnConnectedAsync();
    }
}
