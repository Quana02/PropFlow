using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;

namespace PropFlow.Api.Composition;
public static class ResidentBroadcastDelivery
{
    public static async Task PublishAsync(AuthenticationDbContext accounts, AdministrationDbContext administration,
        IFinanceNotificationStore notifications, Guid eventId, Guid sourceId, string sourceType, string title, string message, CancellationToken ct)
    {
        var residentIds = await administration.UserRoleAssignments.AsNoTracking()
            .Where(x => x.Role.Code == "RESIDENT" && x.Role.IsActive).Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        var recipients = await accounts.UserAccounts.AsNoTracking()
            .Where(x => residentIds.Contains(x.Id) && x.Status == AccountStatus.ACTIVE).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var userId in recipients)
            await notifications.PublishAsync(new(eventId, userId, null, title, message,
                "", sourceType, sourceId, "/resident?tab=notifications"), ct);
    }
}
