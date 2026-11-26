using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;
public sealed partial class PropertyOperationsAuthorizationHttpTests
{
    [Fact]
    public async Task Manager_announcement_search_dates_and_pagination_share_visible_query_without_writes()
    {
        using var scope=Database.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();await db.Database.MigrateAsync();
        var prefix="FILTER-"+Guid.NewGuid().ToString("N")[..8];
        for(var i=1;i<=21;i++){var a=new Announcement(prefix+" "+i,"Nội dung tổng hợp",Manager.Id,Now);a.AddAllResidentsAudience(Now);a.Publish(Manager.Id,Now,"{}");db.Announcements.Add(a);}await db.SaveChangesAsync();
        var before=JsonSerializer.Serialize(await db.Announcements.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync());
        var empty=await Manager.Client.GetFromJsonAsync<JsonElement>("/api/v1/announcements?search=not-a-fixture-title");Assert.Equal(0,empty.GetProperty("total").GetInt32());
        var result=await Manager.Client.GetFromJsonAsync<JsonElement>("/api/v1/announcements?search="+prefix+"&page=2");Assert.Equal(21,result.GetProperty("total").GetInt32());Assert.Single(result.GetProperty("items").EnumerateArray());
        using var invalid=await Manager.Client.GetAsync("/api/v1/announcements?from=2026-12-01T00:00:00Z&to=2026-01-01T00:00:00Z");await Expect(invalid,HttpStatusCode.BadRequest);
        Assert.Equal(before,JsonSerializer.Serialize(await db.Announcements.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync()));
    }
}
