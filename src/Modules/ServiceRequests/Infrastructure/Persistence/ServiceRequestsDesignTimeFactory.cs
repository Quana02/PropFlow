using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

public class ServiceRequestsDesignTimeFactory : IDesignTimeDbContextFactory<ServiceRequestsDbContext>
{
    public ServiceRequestsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ServiceRequestsDbContext>();
        
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling.");
        
        optionsBuilder.UseNpgsql(connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "service_requests"));
        
        return new ServiceRequestsDbContext(optionsBuilder.Options);
    }
}
