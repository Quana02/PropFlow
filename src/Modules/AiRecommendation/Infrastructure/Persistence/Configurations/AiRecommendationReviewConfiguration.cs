using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Configurations;

public class AiRecommendationReviewConfiguration : IEntityTypeConfiguration<AiRecommendationReview>
{
    public void Configure(EntityTypeBuilder<AiRecommendationReview> builder)
    {
        builder.ToTable("ai_recommendation_reviews", table =>
        {
            table.HasCheckConstraint(
                "CK_ai_recommendation_reviews_final_fields",
                "(decision IN ('CONFIRMED', 'CORRECTED', 'OVERRIDDEN') AND final_priority_code IS NOT NULL) OR (decision = 'REJECTED' AND final_priority_code IS NULL AND final_maintenance_required IS NULL AND final_action IS NULL AND final_resource_plan IS NULL)");
            table.HasCheckConstraint(
                "CK_ai_recommendation_reviews_corrected_note",
                "(decision <> 'CORRECTED') OR review_note IS NOT NULL");
        });

        builder.HasKey(review => review.Id);

        builder.Property(review => review.Id).HasColumnName("id");

        builder.Property(review => review.RecommendationId)
            .HasColumnName("recommendation_id")
            .IsRequired();

        builder.Property(review => review.ReviewedBy)
            .HasColumnName("reviewed_by")
            .IsRequired();

        builder.Property(review => review.Decision)
            .HasColumnName("decision")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(review => review.FinalPriorityCode)
            .HasColumnName("final_priority_code")
            .HasMaxLength(30);

        builder.Property(review => review.FinalMaintenanceRequired)
            .HasColumnName("final_maintenance_required");

        builder.Property(review => review.FinalAction)
            .HasColumnName("final_action")
            .HasColumnType("text");

        builder.Property(review => review.FinalResourcePlan)
            .HasColumnName("final_resource_plan")
            .HasColumnType("text");

        builder.Property(review => review.ReviewNote)
            .HasColumnName("review_note")
            .HasColumnType("text");

        builder.Property(review => review.ReviewedAt)
            .HasColumnName("reviewed_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(review => review.RecommendationId)
            .IsUnique();

        builder.HasIndex(review => review.ReviewedBy);

        builder.HasIndex(review => review.Decision);
    }
}
