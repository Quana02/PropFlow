using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;

namespace PropFlow.BillingCycleTests;
public sealed partial class BillingCyclePostgresTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Apartment_floor_limit_rejects_create_update_and_building_reduction(bool configured)
    {
        await using var assets=OperationalAssets();await assets.Database.MigrateAsync();
        await using var apartments=new ApartmentsDbContext(new DbContextOptionsBuilder<ApartmentsDbContext>()
            .UseNpgsql(connection,x=>x.MigrationsHistoryTable("__EFMigrationsHistory","apartments")).Options);
        await apartments.Database.MigrateAsync();
        var now=DateTimeOffset.UtcNow;
        if(configured){assets.Add(new Building("Test building","Test address",now,numberOfFloors:20));await assets.SaveChangesAsync();}
        var type=new ApartmentUnitType("Studio",now);apartments.Add(type);await apartments.SaveChangesAsync();
        await using var factory=new Factory(connection,user,apartment);
        using var manager=factory.Client("MANAGER",actor,"MANAGE_OPERATIONS");
        object Input(int floor, decimal area=25)=>new{unitNumber="FLOOR-TEST",floorNumber=floor,apartmentUnitTypeId=type.Id,usableAreaM2=area,bedroomCount=1,bathroomCount=1};
        var invalid=await manager.PostAsJsonAsync("api/v1/apartments",Input(30));
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        Assert.Contains(configured?"APARTMENT_FLOOR_OUT_OF_RANGE":"BUILDING_NOT_CONFIGURED",await invalid.Content.ReadAsStringAsync());
        Assert.Empty(await apartments.ApartmentUnits.ToArrayAsync());
        if(!configured)return;
        foreach(var area in new[]{0.02m,20m,24.99m,25.001m,100000000m}) {
            var rejected=await manager.PostAsJsonAsync("api/v1/apartments",Input(20,area));
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
            Assert.Contains("APARTMENT_USABLE_AREA_INVALID",await rejected.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Created,(await manager.PostAsJsonAsync("api/v1/apartments",Input(20))).StatusCode);
        var created=await apartments.ApartmentUnits.AsNoTracking().SingleAsync();
        Assert.Equal(20,created.FloorNumber);
        Assert.Equal(25,created.UsableAreaM2);
        Assert.Equal(HttpStatusCode.BadRequest,(await manager.PutAsJsonAsync($"api/v1/apartments/{created.Id}",Input(20,24.99m))).StatusCode);
        Assert.Equal(25,(await apartments.ApartmentUnits.AsNoTracking().SingleAsync()).UsableAreaM2);
        Assert.Equal(HttpStatusCode.NoContent,(await manager.PutAsJsonAsync($"api/v1/apartments/{created.Id}",Input(20,25.5m))).StatusCode);
        Assert.Equal(25.5m,(await apartments.ApartmentUnits.AsNoTracking().SingleAsync()).UsableAreaM2);
        Assert.Equal(HttpStatusCode.BadRequest,(await manager.PutAsJsonAsync($"api/v1/apartments/{created.Id}",Input(21))).StatusCode);
        Assert.Equal(20,(await apartments.ApartmentUnits.AsNoTracking().SingleAsync()).FloorNumber);
        Assert.Equal(HttpStatusCode.NoContent,(await manager.PutAsJsonAsync($"api/v1/apartments/{created.Id}",Input(20))).StatusCode);
        var reduction=await manager.PutAsJsonAsync("api/v1/buildings/current",new{name="Test building",address="Test address",timeZoneId="Asia/Ho_Chi_Minh",numberOfFloors=19});
        Assert.Equal(HttpStatusCode.BadRequest,reduction.StatusCode);
        Assert.Contains("building_floors_below_apartments",await reduction.Content.ReadAsStringAsync());
        Assert.Equal(20,(await assets.Buildings.AsNoTracking().SingleAsync()).NumberOfFloors);
    }
}
