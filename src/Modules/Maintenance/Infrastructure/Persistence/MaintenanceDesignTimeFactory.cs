using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence;

public class MaintenanceDesignTimeFactory : IDesignTimeDbContextFactory<MaintenanceDbContext>
{
    public MaintenanceDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MaintenanceDbContext>();
        
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling.");
        
        optionsBuilder.UseNpgsql(connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "maintenance"));
        
        return new MaintenanceDbContext(optionsBuilder.Options);
    }
}
