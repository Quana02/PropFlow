using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Configurations;

public sealed class FacilityRecommendationRunConfiguration : IEntityTypeConfiguration<FacilityRecommendationRun>
{
    public void Configure(EntityTypeBuilder<FacilityRecommendationRun> b)
    {
        b.ToTable("facility_recommendation_runs"); b.HasKey(x => x.Id);
        b.Property(x => x.Fingerprint).HasMaxLength(100); b.Property(x => x.ResultJson).HasColumnType("jsonb"); b.Property(x => x.Model).HasMaxLength(100);
        b.HasIndex(x => new { x.Actor, x.CreatedAt }); b.HasIndex(x => x.CreatedAt); b.HasIndex(x => new { x.Actor, x.Fingerprint });
        foreach (var property in b.Metadata.GetProperties()) property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
