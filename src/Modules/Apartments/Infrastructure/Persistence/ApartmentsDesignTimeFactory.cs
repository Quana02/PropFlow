using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence;

public class ApartmentsDesignTimeFactory : IDesignTimeDbContextFactory<ApartmentsDbContext>
{
    public ApartmentsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApartmentsDbContext>();
        
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling.");
        
        optionsBuilder.UseNpgsql(connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "apartments"));
        
        return new ApartmentsDbContext(optionsBuilder.Options);
    }
}
