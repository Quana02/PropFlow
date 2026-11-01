using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Communication.Application;

namespace PropFlow.Api.Composition;
public sealed class PricingChangePublisher(AuthenticationDbContext accounts, AdministrationDbContext administration,
    IFinanceNotificationStore notifications) : IPricingChangePublisher
{
    public Task PublishAsync(PricingChangeNotice notice, CancellationToken ct) =>
        ResidentBroadcastDelivery.PublishAsync(accounts, administration, notifications, notice.EventId,
            notice.EventId, "PRICING", notice.Title, notice.Message, ct);
}
