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
    public async Task Apartment_type_presets_preserve_existing_apartments_and_prices(bool differentPrices)
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
        var secondPrice = differentPrices ? 6000000 : 5000000;
        await pricing.SaveAsync(new(Guid.Empty,"MONTHLY",secondPrice,Month,two.Id),actor,default);
        var apartmentTypes = await db.ApartmentUnits.ToDictionaryAsync(x => x.Id, x => x.ApartmentUnitTypeId);
        var prices = await billing.FeeRateRules.ToDictionaryAsync(x => x.Id, x => new { x.ApartmentTypeId, x.UnitRate, x.IsActive });
        const string script = "tools/database/seeds/20261102_commercial_apartment_types.sql";
        var path = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while(path is not null && !File.Exists(Path.Combine(path.FullName, script))) path=path.Parent;
        Assert.True(path is not null, $"Missing repository fixture: {script}");
        var sql=await File.ReadAllTextAsync(Path.Combine(path!.FullName, script));
        await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync();
        async Task Run() { await using var command=new NpgsqlCommand(sql,conn); await command.ExecuteNonQueryAsync(); }
        await Run();
        var types = await db.ApartmentUnitTypes.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        await Run();
        Assert.Equal(types.Select(x => (x.Id, x.UpdatedAt)), (await db.ApartmentUnitTypes.AsNoTracking()
            .OrderBy(x => x.Id).ToArrayAsync()).Select(x => (x.Id, x.UpdatedAt)));
        Assert.Equal(7, types.Length);
        Assert.Equal(35m, types.Single(x => x.Name == "Căn hộ Studio").DefaultUsableAreaM2);
        Assert.Equal(75m, types.Single(x => x.Name == "Căn hộ 2 phòng ngủ").DefaultUsableAreaM2);
        Assert.Equal(apartmentTypes.OrderBy(x => x.Key), (await db.ApartmentUnits.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.ApartmentUnitTypeId)).OrderBy(x => x.Key));
        Assert.Equal(prices.OrderBy(x => x.Key), (await billing.FeeRateRules.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => new { x.ApartmentTypeId, x.UnitRate, x.IsActive })).OrderBy(x => x.Key));
    }
    private sealed class CatalogDirectory(IReadOnlyList<BillingApartmentType> types) : IBillingHouseholdDirectory
    {
        public Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date,CancellationToken ct)=>Task.FromResult<IReadOnlyList<BillingHousehold>>([]);
        public Task<IReadOnlyList<BillingApartmentType>> ApartmentTypesAsync(CancellationToken ct)=>Task.FromResult(types);
    }
}
