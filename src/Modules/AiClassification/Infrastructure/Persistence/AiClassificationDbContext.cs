using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiClassification.Domain.Classifications;

namespace PropFlow.Modules.AiClassification.Infrastructure.Persistence;

public class AiClassificationDbContext : DbContext
{
    public AiClassificationDbContext(DbContextOptions<AiClassificationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AiRequestClassification> AiRequestClassifications => Set<AiRequestClassification>();
    public DbSet<AiClassificationReview> AiClassificationReviews => Set<AiClassificationReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ai_classification");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiClassificationDbContext).Assembly);
    }
}
