using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.AiClassification.Domain.Classifications;

namespace PropFlow.Modules.AiClassification.Infrastructure.Persistence.Configurations;

public class AiClassificationReviewConfiguration : IEntityTypeConfiguration<AiClassificationReview>
{
    public void Configure(EntityTypeBuilder<AiClassificationReview> builder)
    {
        builder.ToTable("ai_classification_reviews", table =>
        {
            table.HasCheckConstraint(
                "CK_ai_classification_reviews_final_category",
                "(decision IN ('CONFIRMED', 'CORRECTED', 'OVERRIDDEN') AND final_category_id IS NOT NULL) OR (decision = 'REJECTED' AND final_category_id IS NULL)");
        });

        builder.HasKey(review => review.Id);

        builder.Property(review => review.Id).HasColumnName("id");

        builder.Property(review => review.ClassificationId)
            .HasColumnName("classification_id")
            .IsRequired();

        builder.Property(review => review.ReviewedBy)
            .HasColumnName("reviewed_by")
            .IsRequired();

        builder.Property(review => review.Decision)
            .HasColumnName("decision")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(review => review.FinalCategoryId)
            .HasColumnName("final_category_id");

        builder.Property(review => review.ReviewNote)
            .HasColumnName("review_note")
            .HasColumnType("text");

        builder.Property(review => review.ReviewedAt)
            .HasColumnName("reviewed_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(review => review.ClassificationId)
            .IsUnique();

        builder.HasIndex(review => review.ReviewedBy);

        builder.HasIndex(review => review.Decision);
    }
}
