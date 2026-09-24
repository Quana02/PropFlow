using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence;

public class BillingDesignTimeFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BillingDbContext>();
        
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling.");
        
        optionsBuilder.UseNpgsql(connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "billing"));
        
        return new BillingDbContext(optionsBuilder.Options);
    }
}
