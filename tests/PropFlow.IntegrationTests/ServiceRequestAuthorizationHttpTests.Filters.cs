using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class ServiceRequestAuthorizationHttpTests
{
    [Fact]
    public async Task Manager_request_multi_filters_share_list_count_summary_and_validate_values()
    {
        var submitted=await Seed();var reviewed=await Seed(reviewed:true);
        var categoryA=new PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories.ServiceRequestCategory("FILTER-A","Loại A",Now);
        var categoryB=new PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories.ServiceRequestCategory("FILTER-B","Loại B",Now);
        using(var scope=host.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
            db.ServiceRequestCategories.AddRange(categoryA,categoryB);
            var a=await db.ServiceRequests.FindAsync(submitted.Id);a!.UpdateDetails(a.Title,a.Description,categoryA.Id,"LOW",Now);
            var b=await db.ServiceRequests.FindAsync(reviewed.Id);b!.UpdateDetails(b.Title,b.Description,categoryB.Id,"HIGH",Now);
            var old=new PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments.ServiceRequestAssignment(b.Id,staffB.Id,manager.Id,Now.AddMinutes(-1));old.EndAsReassigned(Now);
            db.ServiceRequestAssignments.AddRange(old,new(b.Id,staffA.Id,manager.Id,Now));await db.SaveChangesAsync();
        }
        var before=await Snapshot();
        async Task<JsonElement> Read(string query)
        {
            using var response=await manager.Client.GetAsync("/api/v1/manager/service-requests?"+query);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
        }
        var one=await Read("statuses=SUBMITTED");Assert.Equal(1,one.GetProperty("totalCount").GetInt32());
        var all=await Read("statuses=SUBMITTED&statuses=UNDER_REVIEW&priorities=LOW&priorities=HIGH&pageSize=1");
        Assert.Equal(2,all.GetProperty("totalCount").GetInt32());Assert.Equal(2,all.GetProperty("summary").GetProperty("total").GetInt32());
        var selected=await Read("statuses=SUBMITTED&statuses=UNDER_REVIEW&priorities=HIGH");
        Assert.Equal(reviewed.Id,Assert.Single(selected.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(1,selected.GetProperty("summary").GetProperty("total").GetInt32());
        var legacy=await Read("status=SUBMITTED&statuses=UNDER_REVIEW");Assert.Equal(2,legacy.GetProperty("totalCount").GetInt32());
        foreach(var (query,count) in new (string,int)[]{
            ($"categoryIds={categoryA.Id}&categoryIds={categoryB.Id}",2),
            ($"categoryIds={categoryA.Id}&priorities=HIGH",0),
            ($"assignedStaffUserIds={staffA.Id}&assignedStaffUserIds={staffB.Id}",1),
            ($"assignedStaffUserIds={staffB.Id}",0),
            ($"from={Uri.EscapeDataString(Now.AddMinutes(1).ToString("O"))}",0),
            ("view=ACTIVE&statuses=SUBMITTED",0),
            ("view=ACTIVE&statuses=UNDER_REVIEW",1)})
        {var result=await Read(query);Assert.Equal(count,result.GetProperty("totalCount").GetInt32());Assert.Equal(count,result.GetProperty("summary").GetProperty("total").GetInt32());}
        using var options=await manager.Client.GetAsync("/api/v1/manager/service-requests/filter-options");Assert.Equal(HttpStatusCode.OK,options.StatusCode);
        var optionBody=await options.Content.ReadFromJsonAsync<JsonElement>();Assert.Contains(optionBody.GetProperty("categories").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==categoryA.Id);
        Assert.Contains(optionBody.GetProperty("staff").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==staffA.Id);
        Assert.DoesNotContain(optionBody.GetProperty("staff").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==staffB.Id);
        foreach(var actor in new[]{residentA,staffA,admin,accountant})
        {using var denied=await actor.Client.GetAsync("/api/v1/manager/service-requests/filter-options");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        foreach(var q in new[]{"statuses=UNKNOWN","priorities=UNKNOWN","assignedStaffUserIds=00000000-0000-0000-0000-000000000000","from=2026-12-01T00:00:00Z&to=2026-01-01T00:00:00Z",string.Join('&',Enumerable.Repeat("statuses=SUBMITTED",101))})
        {using var r=await manager.Client.GetAsync("/api/v1/manager/service-requests?"+q);Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);}
        Assert.Equal(before,await Snapshot());
    }
}
