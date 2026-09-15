using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Configurations;

public class MaintenanceResultConfiguration : IEntityTypeConfiguration<MaintenanceResult>
{
    public void Configure(EntityTypeBuilder<MaintenanceResult> builder)
    {
        builder.ToTable("maintenance_results", "maintenance", table =>
        {
            table.HasCheckConstraint("CK_maintenance_results_attempt_no", "attempt_no >= 1");
            table.HasCheckConstraint(
                "CK_maintenance_results_reviewed_at",
                "reviewed_at IS NULL OR reviewed_at >= submitted_at");
        });

        builder.HasKey(result => result.Id);
        builder.Property(result => result.Id).HasColumnName("id");

        builder.Property(result => result.MaintenanceTaskId).HasColumnName("maintenance_task_id").IsRequired();
        builder.Property(result => result.AttemptNo)
            .HasColumnName("attempt_no")
            .HasDefaultValue(1)
            .IsRequired();
        builder.HasIndex(result => new { result.MaintenanceTaskId, result.AttemptNo }).IsUnique();

        builder.Property(result => result.SubmittedBy).HasColumnName("submitted_by").IsRequired();

        builder.Property(result => result.Summary)
            .HasColumnName("summary")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(result => result.WorkPerformed)
            .HasColumnName("work_performed")
            .HasColumnType("text");

        builder.Property(result => result.IssueFound)
            .HasColumnName("issue_found")
            .HasColumnType("text");

        builder.Property(result => result.PartsOrResourcesUsed)
            .HasColumnName("parts_or_resources_used")
            .HasColumnType("text");

        builder.Property(result => result.Recommendation)
            .HasColumnName("recommendation")
            .HasColumnType("text");

        builder.Property(result => result.ResultStatus)
            .HasColumnName("result_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(MaintenanceResultStatus.SUBMITTED)
            .IsRequired();
        builder.HasIndex(result => result.ResultStatus);

        builder.Property(result => result.ReviewedBy).HasColumnName("reviewed_by");
        builder.HasIndex(result => result.ReviewedBy);

        builder.Property(result => result.ReviewNote)
            .HasColumnName("review_note")
            .HasColumnType("text");

        builder.Property(result => result.SubmittedAt)
            .HasColumnName("submitted_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(result => result.ReviewedAt)
            .HasColumnName("reviewed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(result => result.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
