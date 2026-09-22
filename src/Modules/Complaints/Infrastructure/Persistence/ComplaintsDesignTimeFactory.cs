using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.Complaints.Infrastructure.Persistence;

public class ComplaintsDesignTimeFactory : IDesignTimeDbContextFactory<ComplaintsDbContext>
{
    public ComplaintsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ComplaintsDbContext>();
        
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling.");
        
        optionsBuilder.UseNpgsql(connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "complaints"));
        
        return new ComplaintsDbContext(optionsBuilder.Options);
    }
}
