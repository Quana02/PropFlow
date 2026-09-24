using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence;

public class CommunicationDesignTimeFactory : IDesignTimeDbContextFactory<CommunicationDbContext>
{
    public CommunicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CommunicationDbContext>();
        
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling.");
        
        optionsBuilder.UseNpgsql(connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "communication"));
        
        return new CommunicationDbContext(optionsBuilder.Options);
    }
}
