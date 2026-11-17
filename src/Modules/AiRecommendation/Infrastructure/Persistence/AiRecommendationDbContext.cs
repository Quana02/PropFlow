using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;

public class AiRecommendationDbContext : DbContext
{
    public AiRecommendationDbContext(DbContextOptions<AiRecommendationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AiRequestRecommendation> AiRequestRecommendations => Set<AiRequestRecommendation>();
    public DbSet<AiRecommendationReview> AiRecommendationReviews => Set<AiRecommendationReview>();
    public DbSet<FacilityRecommendationRun> FacilityRecommendationRuns => Set<FacilityRecommendationRun>();
    public DbSet<PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption.UtilityInsight> UtilityInsights => Set<PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption.UtilityInsight>();
    public DbSet<PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption.UtilityInsightReview> UtilityInsightReviews => Set<PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption.UtilityInsightReview>();
    public DbSet<PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption.UtilityAiAttempt> UtilityAiAttempts => Set<PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption.UtilityAiAttempt>();
    public DbSet<PropFlow.Shared.Messaging.OutboxEvent> UtilityNotices => Set<PropFlow.Shared.Messaging.OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ai_recommendation");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiRecommendationDbContext).Assembly);
    }
}
