using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.PropertyAssets.Application.Parking;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class ParkingPostgresFactAttribute:FactAttribute
{
    public ParkingPostgresFactAttribute(){if(Environment.GetEnvironmentVariable("PROPFLOW_PARKING_POSTGRES")!="1")Skip="Requires explicitly enabled loopback PostgreSQL fixture.";}
}
[Trait("Feature","FE-04-Parking-ScopeExtension")]
public sealed class ParkingPostgresTests
{
    [ParkingPostgresFact]
    public async Task Migration_concurrent_status_updates_and_failed_publication_preserve_data_and_history()
    {
        var target=new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString()){Pooling=false};
        target.Database="postgres";await using var admin=new NpgsqlConnection(target.ConnectionString);await admin.OpenAsync();
        var name="propflow_parking_test_"+Guid.NewGuid().ToString("N");await TestDatabaseSafety.CreateAsync(admin,name);
        try
        {
            target.Database=name;var options=new DbContextOptionsBuilder<PropertyAssetsDbContext>().UseNpgsql(target.ConnectionString,x=>x.MigrationsHistoryTable("__EFMigrationsHistory","property_assets")).Options;
            async Task<T> Call<T>(Func<ParkingWorkspace,Task<T>> action){await using var db=new PropertyAssetsDbContext(options);return await action(new(new EfParkingStore(db),new Reader(),TimeProvider.System));}
            await using(var db=new PropertyAssetsDbContext(options)){Assert.Equal(name,db.Database.GetDbConnection().Database);await db.Database.MigrateAsync();}
            var actor=Guid.NewGuid();var area=await Call(x=>x.CreateAsync(new("Bãi đỗ test"),actor,default));var shape=new ParkingShape(Guid.NewGuid(),"SPACE","P-001",20,20,80,100);
            var trace=new ParkingShape(Guid.NewGuid(),"TRACE","",250,20,100,80,0,null,[[new(0,0),new(1,0),new(1,1),new(0,1)],[new(.1,.1),new(.1,.9),new(.9,.9),new(.9,.1)]]);
            var scale=new ParkingScale(new(20,20),new(120,20),10,"m");
            area=await Call(x=>x.SaveAsync(area.Id,new(area.Version,area.Name,new(600,400,[shape,trace],scale)),actor,default));
            area=await Call(x=>x.PublishAsync(area.Id,new(area.Version),actor,default));
            Assert.Equal(scale,area.Published!.Scale);Assert.Equal(trace.Contours![1],area.Published.Shapes.Single(x=>x.Kind=="TRACE").Contours![1]);
            var outcomes=await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>{
                try{await Call(x=>x.ChangeSpaceAsync(area.Id,shape.Id,new(area.Version,"IN_USE",""),actor,default));return true;}
                catch(ParkingFailure f)when(f.Status==409){return false;}}));
            Assert.Equal(1,outcomes.Count(x=>x));Assert.Equal(7,outcomes.Count(x=>!x));
            area=await Call(x=>x.GetAsync(area.Id,default));var replacement=new ParkingShape(Guid.NewGuid(),"SPACE","NEW",160,20,80,100);
            area=await Call(x=>x.SaveAsync(area.Id,new(area.Version,area.Name,new(600,400,[replacement])),actor,default));
            Assert.Equal(409,(await Assert.ThrowsAsync<ParkingFailure>(()=>Call(x=>x.PublishAsync(area.Id,new(area.Version),actor,default)))).Status);
            var saved=await Call(x=>x.GetAsync(area.Id,default));Assert.Equal(shape.Id,Assert.Single(saved.Published!.Shapes.Where(x=>x.Kind=="SPACE")).Id);Assert.Equal("IN_USE",Assert.Single(saved.Spaces).Status);Assert.Equal(scale,saved.Published.Scale);
            await using(var db=new PropertyAssetsDbContext(options)){Assert.Equal(1,await db.Set<ParkingRevision>().CountAsync());Assert.Equal(5,await db.Set<ParkingAction>().CountAsync());}
        }
        finally{await TestDatabaseSafety.DropAsync(admin,name);}
    }
    private sealed class Reader:IParkingPlanRecognition
    {public Task<ParkingRecognition> ReadAsync(byte[] bytes,string type,int page,CancellationToken ct)=>Task.FromResult(new ParkingRecognition(null,"Không gọi AI trong test PostgreSQL."));}
}
