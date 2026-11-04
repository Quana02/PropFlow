using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PropFlow.Modules.Communication.Application;

namespace PropFlow.Modules.Communication.Presentation;

[Authorize(Roles = "RESIDENT")]
public sealed class ResidentNotificationHub : Hub { }

public sealed class FinanceNotificationRealtime(IHubContext<ResidentNotificationHub> hub) : IFinanceNotificationRealtime
{
    public Task CreatedAsync(Guid recipientUserId, FinanceNotificationUpdate notice, CancellationToken ct) =>
        hub.Clients.User(recipientUserId.ToString()).SendAsync("FinanceNotificationCreated", notice, ct);
}
