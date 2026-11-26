using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
public sealed class RemoteOnlineSignDesignTimeFactory : IDesignTimeDbContextFactory<RemoteOnlineSignDbContext>
{
    public RemoteOnlineSignDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase to scaffold a RemoteOnlineSign migration.");
        return new(new DbContextOptionsBuilder<RemoteOnlineSignDbContext>().UseNpgsql(connection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "remote_online_sign")).Options);
    }
}
