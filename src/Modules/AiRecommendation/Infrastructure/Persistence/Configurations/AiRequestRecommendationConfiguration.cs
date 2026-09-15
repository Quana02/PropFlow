using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.Persistence.Configurations;

public class AiRequestRecommendationConfiguration : IEntityTypeConfiguration<AiRequestRecommendation>
{
    public void Configure(EntityTypeBuilder<AiRequestRecommendation> builder)
    {
        builder.ToTable("ai_request_recommendations", table =>
        {
            table.HasCheckConstraint("CK_ai_request_recommendations_attempt_no", "attempt_no >= 1");
            table.HasCheckConstraint("CK_ai_request_recommendations_tokens", "(input_tokens IS NULL OR input_tokens >= 0) AND (output_tokens IS NULL OR output_tokens >= 0) AND (total_tokens IS NULL OR total_tokens >= 0)");
            table.HasCheckConstraint("CK_ai_request_recommendations_latency", "latency_ms IS NULL OR latency_ms >= 0");
            table.HasCheckConstraint(
                "CK_ai_request_recommendations_success_fields",
                "(run_status <> 'SUCCESS') OR (suggested_priority_code IS NOT NULL AND error_message IS NULL)");
            table.HasCheckConstraint(
                "CK_ai_request_recommendations_failed_fields",
                "(run_status <> 'FAILED') OR (error_message IS NOT NULL AND suggested_priority_code IS NULL AND maintenance_recommended IS NULL AND recommended_action IS NULL AND recommended_resource_summary IS NULL AND recommended_resources IS NULL AND reasoning_summary IS NULL)");
        });

        builder.HasKey(recommendation => recommendation.Id);

        builder.Property(recommendation => recommendation.Id).HasColumnName("id");

        builder.Property(recommendation => recommendation.ServiceRequestId)
            .HasColumnName("service_request_id")
            .IsRequired();

        builder.Property(recommendation => recommendation.AttemptNo)
            .HasColumnName("attempt_no")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(recommendation => recommendation.Provider)
            .HasColumnName("provider")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(recommendation => recommendation.ModelName)
            .HasColumnName("model_name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(recommendation => recommendation.PromptVersion)
            .HasColumnName("prompt_version")
            .HasMaxLength(50);

        builder.Property(recommendation => recommendation.SuggestedPriorityCode)
            .HasColumnName("suggested_priority_code")
            .HasMaxLength(30);

        builder.Property(recommendation => recommendation.MaintenanceRecommended)
            .HasColumnName("maintenance_recommended");

        builder.Property(recommendation => recommendation.RecommendedAction)
            .HasColumnName("recommended_action")
            .HasColumnType("text");

        builder.Property(recommendation => recommendation.RecommendedResourceSummary)
            .HasColumnName("recommended_resource_summary")
            .HasColumnType("text");

        builder.Property(recommendation => recommendation.RecommendedResourcesJson)
            .HasColumnName("recommended_resources")
            .HasColumnType("jsonb");

        builder.Property(recommendation => recommendation.ReasoningSummary)
            .HasColumnName("reasoning_summary")
            .HasColumnType("text");

        builder.Property(recommendation => recommendation.InputHash)
            .HasColumnName("input_hash")
            .HasMaxLength(128);

        builder.Property(recommendation => recommendation.InputTokens)
            .HasColumnName("input_tokens");

        builder.Property(recommendation => recommendation.OutputTokens)
            .HasColumnName("output_tokens");

        builder.Property(recommendation => recommendation.TotalTokens)
            .HasColumnName("total_tokens");

        builder.Property(recommendation => recommendation.RunStatus)
            .HasColumnName("run_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(recommendation => recommendation.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        builder.Property(recommendation => recommendation.LatencyMs)
            .HasColumnName("latency_ms");

        builder.Property(recommendation => recommendation.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(recommendation => recommendation.ServiceRequestId);

        builder.HasIndex(recommendation => recommendation.RunStatus);

        builder.HasIndex(recommendation => recommendation.SuggestedPriorityCode);

        builder.HasIndex(recommendation => new { recommendation.ServiceRequestId, recommendation.AttemptNo })
            .IsUnique();

        builder.HasOne(recommendation => recommendation.Review)
            .WithOne(review => review.Recommendation)
            .HasForeignKey<AiRecommendationReview>(review => review.RecommendationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
