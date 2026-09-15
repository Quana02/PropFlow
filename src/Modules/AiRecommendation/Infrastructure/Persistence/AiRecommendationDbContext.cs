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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ai_recommendation");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiRecommendationDbContext).Assembly);
    }
}
