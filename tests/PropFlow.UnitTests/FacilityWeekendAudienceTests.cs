using Microsoft.EntityFrameworkCore;
using PropFlow.Api.Composition;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.UnitTests;

public sealed class FacilityWeekendAudienceTests
{
    [Fact]
    public async Task Only_active_resident_accounts_with_current_residency_receive_the_web_notice()
    {
        await using var accounts=new AuthenticationDbContext(new DbContextOptionsBuilder<AuthenticationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        UserAccount User(string name,AccountStatus status=AccountStatus.ACTIVE)=>new(name,"test-hash",name,DateTimeOffset.UtcNow,status:status);
        var eligible=User("resident");var inactive=User("disabled",AccountStatus.DISABLED);var former=User("former");var manager=User("manager");var inactiveProfile=User("inactive-profile");
        accounts.UserAccounts.AddRange(eligible,inactive,former,manager,inactiveProfile);await accounts.SaveChangesAsync();
        ResidentAccountScopeItem Item(Guid? user,bool active=true)=>new(Guid.NewGuid(),user,"TEST","Test resident",null,active,[]);
        var directory=new Directory([Item(eligible.Id),Item(inactive.Id),Item(former.Id),Item(manager.Id),Item(inactiveProfile.Id,false),Item(null)]);
        var publisher=new Publisher();var residences=new Residences([eligible.Id,inactive.Id,manager.Id,inactiveProfile.Id]);
        var delivery=new FacilityWeekendNoticeDelivery(directory,residences,new Roles([eligible.Id,inactive.Id,former.Id,inactiveProfile.Id]),accounts,publisher);
        var notice=new FacilityWeekendNotice(Guid.NewGuid(),new(2026,11,21),Guid.NewGuid(),"Cuối tuần","Thư viện mở cửa hôm nay.");
        Assert.Equal(1,await delivery.DeliverAsync(notice,default));
        var result=Assert.Single(publisher.Notices);Assert.Equal(eligible.Id,result.UserId);Assert.Null(result.Email);
        Assert.Equal(FacilityWeekendNotice.SourceType,result.SourceType);Assert.Equal(notice.ActionPath,result.ActionPath);
        Assert.Equal(notice.LocalDate,residences.Day);
    }
    private sealed class Directory(ResidentAccountScopeItem[] items):IResidentAccountScope
    {
        public Task<ResidentAccountScopePage> SearchAsync(string? search,int pageIndex,int pageSize,CancellationToken ct)=>Task.FromResult(new ResidentAccountScopePage(items,items.Length,pageIndex));
        public Task<Guid?> FindActiveAccountAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid,ResidentAccountScopeItem>> GetByAccountIdsAsync(IReadOnlyCollection<Guid> users,CancellationToken ct)=>Task.FromResult<IReadOnlyDictionary<Guid,ResidentAccountScopeItem>>(items.Where(item=>item.UserId is not null&&users.Contains(item.UserId.Value)).ToDictionary(item=>item.UserId!.Value));
        public Task<bool> IsActiveAccountAsync(Guid userId,CancellationToken ct)=>Task.FromResult(items.Any(item=>item.UserId==userId&&item.IsActive));
        public Task<bool> LockActiveLinkAsync(Guid resident,Guid user,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Roles(Guid[] users):IAccountAccess
    {
        public Task<Guid[]> GetResidentUserIdsAsync(Guid[] ids,CancellationToken ct)=>Task.FromResult(ids.Intersect(users).ToArray());
        public Task<AccountAccess?> GetAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task GrantResidentAsync(Guid id,DateTimeOffset now,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Residences(Guid[] users):IResidentResidenceSource
    {
        public DateOnly Day;
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid id,DateOnly day,CancellationToken ct)
        {Day=day;return Task.FromResult<ActiveResidentResidence?>(users.Contains(id)?new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()):null);}
    }
    private sealed class Publisher:IFinanceNotificationStore
    {
        public List<FinanceNotification> Notices=[];
        public Task PublishAsync(FinanceNotification notice,CancellationToken ct){Notices.Add(notice);return Task.CompletedTask;}
    }
}
