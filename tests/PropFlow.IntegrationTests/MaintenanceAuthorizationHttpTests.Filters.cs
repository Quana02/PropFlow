using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;
public sealed partial class MaintenanceAuthorizationHttpTests
{
    [Fact]
    public async Task Manager_maintenance_multi_queries_preserve_count_history_correlation_and_legacy()
    {
        var prefix="FILTER-"+Guid.NewGuid().ToString("N")[..8];
        var a=new MaintenanceTask(prefix+"1",prefix+" A",manager.Id,Now,facilityId:facility.Id,equipmentId:equipment.Id,priorityCode:"HIGH");
        var b=new MaintenanceTask(prefix+"2",prefix+" B",manager.Id,Now,facilityId:otherFacility.Id,equipmentId:otherEquipment.Id,priorityCode:"LOW");
        var s1=new MaintenanceSchedule(prefix+"S1",prefix+" S1",Now.AddDays(1),manager.Id,Now,facility.Id,equipment.Id);
        var s2=new MaintenanceSchedule(prefix+"S2",prefix+" S2",Now.AddDays(2),manager.Id,Now,otherFacility.Id,otherEquipment.Id);
        using(var scope=database.Factory.Services.CreateScope())
        {var db=scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();db.MaintenanceTasks.AddRange(a,b);db.MaintenanceSchedules.AddRange(s1,s2);await db.SaveChangesAsync();}
        var before=await Snapshot();
        async Task<JsonElement> Read(string route){using var r=await manager.Client.GetAsync(Root+route);await ExpectStatus(r,HttpStatusCode.OK);return(await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();}
        var selected=await Read("tasks?searchKeyword="+prefix+"&priorities=HIGH");Assert.Equal(1,selected.GetProperty("totalCount").GetInt32());
        foreach(var route in new[]{"tasks","schedules","history"})
        {
            var body=await Read(route+$"?searchKeyword={prefix}&facilityIds={facility.Id}&facilityIds={otherFacility.Id}&pageSize=1");
            var page=route=="history"?body.GetProperty("page"):body;Assert.Equal(2,page.GetProperty("totalCount").GetInt32());Assert.Single(page.GetProperty("items").EnumerateArray());
            var empty=await Read(route+$"?searchKeyword={prefix}&facilityIds={facility.Id}&equipmentIds={otherEquipment.Id}");Assert.Equal(0,(route=="history"?empty.GetProperty("page"):empty).GetProperty("totalCount").GetInt32());
            foreach(var invalid in new[]{"statuses=999","facilityIds=00000000-0000-0000-0000-000000000000",string.Join('&',Enumerable.Repeat("equipmentIds="+equipment.Id,101))})
            {using var r=await manager.Client.GetAsync(Root+route+"?"+invalid);await ExpectStatus(r,HttpStatusCode.BadRequest);}
        }
        Assert.Equal(before,await Snapshot());
    }
}
