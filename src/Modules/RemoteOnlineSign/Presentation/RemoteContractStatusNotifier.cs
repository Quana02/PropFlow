using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.RemoteOnlineSign.Application;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.RemoteOnlineSign.Presentation;

public sealed class RemoteContractStatusNotifier(
    IHubContext<RemoteOnlineSignHub> hub,
    RemoteOnlineSignDbContext db,
    IAccountAccess accountAccess,
    IResidentAccountScope residents) : IRemoteContractStatusNotifier
{
    public async Task NotifyAsync(RemoteContractStatusChanged change, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking()
            .Where(x => x.Id == change.ContractId)
            .Select(x => new { x.CreatorId, x.ResidentId })
            .SingleOrDefaultAsync(ct);
        if (contract is null) return;

        var recipients = new HashSet<Guid> { contract.CreatorId };
        if (contract.ResidentId is { } residentId
            && (await accountAccess.GetResidentUserIdsAsync([residentId], ct)).Contains(residentId)
            && await residents.IsActiveAccountAsync(residentId, ct))
            recipients.Add(residentId);

        if (recipients.Count != 0)
            await hub.Clients.Users(recipients.Select(x => x.ToString()).ToArray())
                .SendAsync("ContractStatusChanged", change, ct);
    }
}
