using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption;
using PropFlow.Shared.Messaging;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Configurations;

public sealed class UtilityInsightConfiguration : IEntityTypeConfiguration<UtilityInsight>, IEntityTypeConfiguration<UtilityInsightReview>,
    IEntityTypeConfiguration<UtilityAiAttempt>, IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<UtilityInsight> b)
    {
        b.ToTable("utility_insights"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.ApartmentId, x.Month }).IsUnique();
        b.Property(x => x.UnitNumber).HasMaxLength(100); b.Property(x => x.Fingerprint).HasMaxLength(100);
        b.Property(x => x.MethodVersion).HasMaxLength(80); b.Property(x => x.AnalysisJson).HasColumnType("jsonb");
        b.Property(x => x.Review).HasMaxLength(20); b.Property(x => x.ReviewNote).HasMaxLength(500);
        b.Property(x => x.AiExplanation).HasMaxLength(900); b.Property(x => x.AiMode).HasMaxLength(30);
        b.HasIndex(x => new { x.Month, x.Review });
        b.ToTable(t => { t.HasCheckConstraint("ck_utility_review", "review IN ('PENDING','CONFIRMED','DISMISSED')"); t.HasCheckConstraint("ck_utility_revision", "revision > 0"); });
        Columns(b);
    }
    public void Configure(EntityTypeBuilder<UtilityInsightReview> b)
    {
        b.ToTable("utility_insight_reviews"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.InsightId, x.Revision }).IsUnique();
        b.HasOne<UtilityInsight>().WithMany().HasForeignKey(x => x.InsightId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Fingerprint).HasMaxLength(100); b.Property(x => x.Decision).HasMaxLength(20); b.Property(x => x.Note).HasMaxLength(500);
        Columns(b);
    }
    public void Configure(EntityTypeBuilder<UtilityAiAttempt> b)
    {
        b.ToTable("utility_ai_attempts"); b.HasKey(x => x.Id); b.Property(x => x.Model).HasMaxLength(100);
        b.HasIndex(x => new { x.Actor, x.CreatedAt }); b.HasIndex(x => x.CreatedAt);
        Columns(b);
    }
    public void Configure(EntityTypeBuilder<OutboxEvent> b)
    {
        b.ToTable("utility_notification_outbox"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100); b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.Status).HasMaxLength(20); b.Property(x => x.LastErrorType).HasMaxLength(200);
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        Columns(b);
    }
    private static void Columns<T>(EntityTypeBuilder<T> b) where T : class
    {
        foreach (var property in b.Metadata.GetProperties())
            property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
