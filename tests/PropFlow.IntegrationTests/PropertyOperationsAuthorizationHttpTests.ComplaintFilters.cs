using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;
public sealed partial class PropertyOperationsAuthorizationHttpTests
{
    [Fact]
    public async Task Manager_complaint_multi_filters_correlate_active_staff_and_preserve_other_role_scope()
    {
        var home=await Database.SeedResidentAsync(ContactEmail());var prefix="FILTER-"+Guid.NewGuid().ToString("N")[..8];
        var a=new Complaint(prefix+"1",home.ResidentId,home.ResidencyId!.Value,prefix+" A","Mô tả thử",Now);
        var b=new Complaint(prefix+"2",home.ResidentId,home.ResidencyId.Value,prefix+" B","Mô tả thử",Now);b.MarkUnderReview(Now);
        using var scope=Database.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ComplaintsDbContext>();await db.Database.MigrateAsync();
        var active=new ComplaintFollowup(b.Id,fixture.Actor("STAFF").Id,Manager.Id,"Thử xử lý",Now);
        db.Complaints.AddRange(a,b);db.ComplaintFollowups.Add(active);await db.SaveChangesAsync();
        var before=JsonSerializer.Serialize(await db.Complaints.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync());
        async Task<JsonElement> Read(string query){using var r=await Manager.Client.GetAsync("/api/v1/complaints?search="+prefix+"&"+query);await Expect(r,HttpStatusCode.OK);return(await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();}
        var one=await Read("statuses=SUBMITTED");Assert.Equal(1,one.GetProperty("total").GetInt32());
        foreach(var (q,count) in new(string,int)[]{("statuses=SUBMITTED&statuses=UNDER_REVIEW",2),("status=SUBMITTED&statuses=UNDER_REVIEW",2),($"assignedStaffUserIds={fixture.Actor("STAFF").Id}",1),("hasActiveFollowup=false",1),("statuses=SUBMITTED&hasActiveFollowup=true",0)})Assert.Equal(count,(await Read(q)).GetProperty("total").GetInt32());
        foreach(var q in new[]{"statuses=INVALID","assignedStaffUserIds=00000000-0000-0000-0000-000000000000","from=2026-12-31T00:00:00Z&to=2026-01-01T00:00:00Z"})await Check(Manager.Client,HttpMethod.Get,"complaints?"+q,null,HttpStatusCode.BadRequest);
        await Check(fixture.Actor("STAFF").Client,HttpMethod.Get,"complaints?statuses=SUBMITTED",null,HttpStatusCode.Forbidden);
        Assert.Equal(before,JsonSerializer.Serialize(await db.Complaints.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync()));
    }
}
