using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Api.Composition;

public sealed class FacilityWeekendNoticeDelivery(IResidentAccountScope residents, IResidentResidenceSource residences,
    IAccountAccess roles, AuthenticationDbContext accounts, IFinanceNotificationStore notifications)
    : IFacilityWeekendNoticeDelivery
{
    public async Task<int> DeliverAsync(FacilityWeekendNotice notice, CancellationToken ct)
    {
        const int size = 200;
        var count = 0;
        for (var page = 1; ; page++)
        {
            var batch = await residents.SearchAsync(null, page, size, ct);
            var candidates = batch.Items.Where(x => x.IsActive && x.UserId != null).Select(x => x.UserId!.Value).Distinct().ToArray();
            var assigned = await roles.GetResidentUserIdsAsync(candidates, ct);
            var active = await accounts.UserAccounts.AsNoTracking()
                .Where(x => assigned.Contains(x.Id) && x.Status == AccountStatus.ACTIVE).Select(x => x.Id).ToArrayAsync(ct);
            foreach (var userId in active)
            {
                if (await residences.FindActiveAsync(userId, notice.LocalDate, ct) is null) continue;
                await notifications.PublishAsync(new(notice.EventId, userId, null, notice.Title, notice.Message, "",
                    FacilityWeekendNotice.SourceType, notice.FacilityId, notice.ActionPath), ct);
                count++;
            }
            if (page * size >= batch.TotalCount) break;
        }
        return count;
    }
}
