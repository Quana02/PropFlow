using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

public class PropertyAssetsDesignTimeFactory : IDesignTimeDbContextFactory<PropertyAssetsDbContext>
{
    public PropertyAssetsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PropertyAssetsDbContext>();
        
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling.");
        
        optionsBuilder.UseNpgsql(connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "property_assets"));
        
        return new PropertyAssetsDbContext(optionsBuilder.Options);
    }
}
