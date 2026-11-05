using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Infrastructure.Finance;

namespace PropFlow.BillingCycleTests;
public sealed partial class BillingCyclePostgresTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apartment_catalog_merge_preserves_prices_and_rejects_conflicts(bool conflict)
    {
        await using var db = new ApartmentsDbContext(new DbContextOptionsBuilder<ApartmentsDbContext>()
            .UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "apartments")).Options);
        await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var one = new ApartmentUnitType("1 phòng ngủ", now);
        var two = new ApartmentUnitType("2 phòng ngủ", now);
        db.AddRange(one,two);
        db.Add(new ApartmentUnit("CAT-1",1,now,one.Id,50,1));
        db.Add(new ApartmentUnit("CAT-2",2,now,two.Id,70,2));
        await db.SaveChangesAsync();
        await using var billing = Billing();
        var pricing = new RentalPricing(billing, new CatalogDirectory([new(one.Id,one.Name),new(two.Id,two.Name)]));
        // A catalog type may be priced before any apartment of that type exists.
        await pricing.SaveAsync(new(Guid.Empty,"MONTHLY",5000000,Month,one.Id),actor,default);
        await pricing.SaveAsync(new(Guid.Empty,"MONTHLY",conflict?6000000:5000000,Month,two.Id),actor,default);
        var path = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while(path is not null && !File.Exists(Path.Combine(path.FullName,"docs/deployment/20261016-four-apartment-types.sql"))) path=path.Parent;
        var sql=await File.ReadAllTextAsync(Path.Combine(path!.FullName,"docs/deployment/20261016-four-apartment-types.sql"));
        await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync();
        async Task Run() { await using var command=new NpgsqlCommand(sql,conn); await command.ExecuteNonQueryAsync(); }
        if(conflict) {
            var error=await Assert.ThrowsAsync<PostgresException>(Run);
            Assert.Contains("Conflicting rental prices",error.MessageText);
            await using var rollback=new NpgsqlCommand("ROLLBACK",conn); await rollback.ExecuteNonQueryAsync();
            Assert.Equal(2,await db.ApartmentUnitTypes.CountAsync());
            Assert.Equal(2,await billing.FeeRateRules.CountAsync(x=>x.IsActive));
            return;
        }
        await Run(); await Run();
        var types=await db.ApartmentUnitTypes.AsNoTracking().OrderBy(x=>x.Name).ToArrayAsync();
        Assert.Equal(new[]{"Căn hộ thông thường","Officetel","Penthouse","Studio"},types.Select(x=>x.Name));
        var standard=types.Single(x=>x.Name=="Căn hộ thông thường");
        Assert.All(await db.ApartmentUnits.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(standard.Id,x.ApartmentUnitTypeId));
        var rates=await billing.FeeRateRules.AsNoTracking().ToArrayAsync();
        Assert.Equal(2,rates.Length); Assert.Single(rates.Where(x=>x.IsActive));
        Assert.All(rates,x=>{Assert.Equal(5000000,x.UnitRate);Assert.Equal(standard.Id,x.ApartmentTypeId);});
    }
    private sealed class CatalogDirectory(IReadOnlyList<BillingApartmentType> types) : IBillingHouseholdDirectory
    {
        public Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date,CancellationToken ct)=>Task.FromResult<IReadOnlyList<BillingHousehold>>([]);
        public Task<IReadOnlyList<BillingApartmentType>> ApartmentTypesAsync(CancellationToken ct)=>Task.FromResult(types);
    }
}
