using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class UtilityInsightsDesignTimeFactory : IDesignTimeDbContextFactory<AiRecommendationDbContext>
{
    public AiRecommendationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__PropFlowDatabase for EF tooling.");
        return new(new DbContextOptionsBuilder<AiRecommendationDbContext>().UseNpgsql(connection,
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "ai_recommendation")).Options);
    }
}
