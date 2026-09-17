using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence;

public sealed class AuthenticationDesignTimeFactory : IDesignTimeDbContextFactory<AuthenticationDbContext>
{
    public AuthenticationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling. This factory does not apply migrations.");
        return new(new DbContextOptionsBuilder<AuthenticationDbContext>().UseNpgsql(connection,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "auth")).Options);
    }
}
