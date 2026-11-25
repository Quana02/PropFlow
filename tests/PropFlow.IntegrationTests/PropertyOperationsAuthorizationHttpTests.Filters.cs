using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class PropertyOperationsAuthorizationHttpTests
{
    [Fact]
    public async Task Manager_equipment_multi_filters_include_building_level_preserve_legacy_and_scope()
    {
        var prefix="EQFILTER-"+Guid.NewGuid().ToString("N")[..8];
        var t1=new PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentType(prefix+" A",Manager.Id,Now);
        var t2=new PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentType(prefix+" B",Manager.Id,Now);
        var f1=new Facility(prefix+"-F1",prefix+" F1",Now);var f2=new Facility(prefix+"-F2",prefix+" F2",Now);
        var a=new PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment(prefix+"-1",prefix+" A",Now,f1.Id,t1.Id,serialNumber:"SERIAL-ONLY-"+prefix);
        var b=new PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment(prefix+"-2",prefix+" B",Now,f2.Id,t2.Id);
        var c=new PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment(prefix+"-3",prefix+" C",Now,equipmentTypeId:t1.Id);
        b.Deactivate(Manager.Id,Now);
        using(var scope=Database.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
            db.Facilities.AddRange(f1,f2);db.EquipmentTypes.AddRange(t1,t2);db.Equipment.AddRange(a,b,c);await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<PropFlow.Modules.Maintenance.Infrastructure.Persistence.MaintenanceDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.ServiceRequestsDbContext>().Database.MigrateAsync();
        }
        var before=await AssetSnapshot();
        foreach(var (q,ids) in new (string,Guid[])[] {
            ("statuses=ACTIVE&statuses=INACTIVE",[a.Id,b.Id,c.Id]),
            ($"facilityIds={f1.Id}&includeBuildingLevel=true",[a.Id,c.Id]),
            ($"facilityId={f1.Id}&facilityIds={f2.Id}",[a.Id,b.Id]),
            ($"equipmentTypeIds={t1.Id}&equipmentTypeIds={t2.Id}&statuses=INACTIVE",[b.Id]),
            ($"facilityIds={f2.Id}&equipmentTypeIds={t1.Id}",[]),
            ("buildingLevelOnly=true",[c.Id]),
            ($"buildingLevelOnly=true&facilityId={f1.Id}",[]) })
        {
            var result=await Manager.Client.GetFromJsonAsync<JsonElement>("/api/v1/equipments?searchKeyword="+prefix+"&"+q);
            Assert.Equal(ids.Length,result.GetProperty("totalCount").GetInt32());
            Assert.Equal(ids.Order(),result.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()).Order());
            Assert.Equal(ids.Length,result.GetProperty("summary").GetProperty("total").GetInt32());
        }
        var paged=await Manager.Client.GetFromJsonAsync<JsonElement>($"/api/v1/equipments?searchKeyword={prefix}&statuses=ACTIVE&statuses=INACTIVE&pageSize=1&pageIndex=2");
        Assert.Equal(3,paged.GetProperty("totalCount").GetInt32());Assert.Single(paged.GetProperty("items").EnumerateArray());
        var serial=await Manager.Client.GetFromJsonAsync<JsonElement>("/api/v1/equipments?searchKeyword=SERIAL-ONLY-"+prefix);
        Assert.Equal(a.Id,Assert.Single(serial.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        foreach(var q in new[]{"statuses=999","facilityIds=00000000-0000-0000-0000-000000000000","equipmentTypeIds=bad",string.Join('&',Enumerable.Repeat("facilityIds="+f1.Id,101))})
            await Check(Manager.Client,HttpMethod.Get,"equipments?"+q,null,HttpStatusCode.BadRequest);
        foreach(var role in new[]{"STAFF","RESIDENT","ACCOUNTANT","ADMIN"})
        {
            using var response=await fixture.Actor(role).Client.GetAsync($"/api/v1/equipments?searchKeyword={prefix}&facilityIds={f1.Id}&includeBuildingLevel=true");
            if(role=="STAFF") {await Expect(response,HttpStatusCode.OK);Assert.Equal(0,(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totalCount").GetInt32());}
            else await Expect(response,HttpStatusCode.Forbidden);
        }
        Assert.Equal(before,await AssetSnapshot());
    }
    [Fact]
    public async Task Manager_facility_multi_filters_use_OR_within_AND_between_and_share_count_summary()
    {
        var prefix = "FILTER-" + Guid.NewGuid().ToString("N")[..8];
        var t1 = new FacilityType(prefix+" A", Manager.Id, Now);
        var t2 = new FacilityType(prefix+" B", Manager.Id, Now);
        var a = new Facility(prefix+"-1",prefix+" A",Now,t1.Id,"Vị trí đặc biệt",initialStatus:MasterDataStatus.ACTIVE);
        var b = new Facility(prefix+"-2",prefix+" B",Now,t2.Id,initialStatus:MasterDataStatus.INACTIVE);
        var c = new Facility(prefix+"-3",prefix+" C",Now,t1.Id,initialStatus:MasterDataStatus.UNAVAILABLE);
        using(var scope=Database.Factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
            db.FacilityTypes.AddRange(t1,t2);db.Facilities.AddRange(a,b,c);await db.SaveChangesAsync();
        }
        var before=await AssetSnapshot();
        async Task<JsonElement> Read(string query)
        {
            using var response=await Manager.Client.GetAsync("/api/v1/facilities?searchKeyword="+prefix+query);
            await Expect(response,HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
        }
        var one=await Read("&statuses=INACTIVE");
        Assert.Equal(1,one.GetProperty("totalCount").GetInt32());
        Assert.Equal(b.Id,one.GetProperty("items")[0].GetProperty("id").GetGuid());
        var union=await Read($"&status=ACTIVE&statuses=INACTIVE&statuses=INACTIVE&facilityTypeIds={t1.Id}&facilityTypeIds={t2.Id}&pageSize=1");
        Assert.Equal(2,union.GetProperty("totalCount").GetInt32());
        Assert.Equal(2,union.GetProperty("summary").GetProperty("total").GetInt32());
        Assert.Equal(1,union.GetProperty("summary").GetProperty("inactive").GetInt32());
        var intersection=await Read($"&statuses=ACTIVE&statuses=INACTIVE&facilityTypeIds={t1.Id}");
        Assert.Equal(a.Id,Assert.Single(intersection.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var location=await Manager.Client.GetAsync("/api/v1/facilities?searchKeyword="+Uri.EscapeDataString("Vị trí đặc biệt"));
        await Expect(location,HttpStatusCode.OK);
        Assert.Contains((await location.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==a.Id);
        foreach(var invalid in new[]{"statuses=999","facilityTypeIds=00000000-0000-0000-0000-000000000000",string.Join('&',Enumerable.Repeat("statuses=ACTIVE",101))})
            await Check(Manager.Client,HttpMethod.Get,"facilities?"+invalid,null,HttpStatusCode.BadRequest);
        Assert.Equal(before,await AssetSnapshot());
    }
}
