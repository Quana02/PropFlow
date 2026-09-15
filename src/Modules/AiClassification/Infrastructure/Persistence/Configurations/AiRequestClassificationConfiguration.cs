using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.AiClassification.Domain.Classifications;

namespace PropFlow.Modules.AiClassification.Infrastructure.Persistence.Configurations;

public class AiRequestClassificationConfiguration : IEntityTypeConfiguration<AiRequestClassification>
{
    public void Configure(EntityTypeBuilder<AiRequestClassification> builder)
    {
        builder.ToTable("ai_request_classifications", table =>
        {
            table.HasCheckConstraint("CK_ai_request_classifications_attempt_no", "attempt_no >= 1");
            table.HasCheckConstraint("CK_ai_request_classifications_confidence_score", "confidence_score IS NULL OR (confidence_score >= 0 AND confidence_score <= 1)");
            table.HasCheckConstraint("CK_ai_request_classifications_tokens", "(input_tokens IS NULL OR input_tokens >= 0) AND (output_tokens IS NULL OR output_tokens >= 0) AND (total_tokens IS NULL OR total_tokens >= 0)");
            table.HasCheckConstraint("CK_ai_request_classifications_latency", "latency_ms IS NULL OR latency_ms >= 0");
            table.HasCheckConstraint(
                "CK_ai_request_classifications_success_fields",
                "(run_status <> 'SUCCESS') OR (predicted_category_id IS NOT NULL AND error_message IS NULL)");
            table.HasCheckConstraint(
                "CK_ai_request_classifications_failed_fields",
                "(run_status <> 'FAILED') OR (error_message IS NOT NULL AND predicted_category_id IS NULL AND confidence_score IS NULL AND reasoning_summary IS NULL)");
        });

        builder.HasKey(classification => classification.Id);

        builder.Property(classification => classification.Id).HasColumnName("id");

        builder.Property(classification => classification.ServiceRequestId)
            .HasColumnName("service_request_id")
            .IsRequired();

        builder.Property(classification => classification.AttemptNo)
            .HasColumnName("attempt_no")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(classification => classification.Provider)
            .HasColumnName("provider")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(classification => classification.ModelName)
            .HasColumnName("model_name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(classification => classification.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(50);

        builder.Property(classification => classification.PredictedCategoryId)
            .HasColumnName("predicted_category_id");

        builder.Property(classification => classification.ConfidenceScore)
            .HasColumnName("confidence_score")
            .HasColumnType("numeric(5,4)");

        builder.Property(classification => classification.ReasoningSummary)
            .HasColumnName("reasoning_summary")
            .HasColumnType("text");

        builder.Property(classification => classification.InputHash)
            .HasColumnName("input_hash")
            .HasMaxLength(128);

        builder.Property(classification => classification.InputTokens)
            .HasColumnName("input_tokens");

        builder.Property(classification => classification.OutputTokens)
            .HasColumnName("output_tokens");

        builder.Property(classification => classification.TotalTokens)
            .HasColumnName("total_tokens");

        builder.Property(classification => classification.RunStatus)
            .HasColumnName("run_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(classification => classification.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        builder.Property(classification => classification.LatencyMs)
            .HasColumnName("latency_ms");

        builder.Property(classification => classification.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(classification => classification.ServiceRequestId);

        builder.HasIndex(classification => classification.PredictedCategoryId);

        builder.HasIndex(classification => classification.RunStatus);

        builder.HasIndex(classification => new { classification.ServiceRequestId, classification.AttemptNo })
            .IsUnique();

        builder.HasOne(classification => classification.Review)
            .WithOne(review => review.Classification)
            .HasForeignKey<AiClassificationReview>(review => review.ClassificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
