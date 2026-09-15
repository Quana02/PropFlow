using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Configurations;

public class MaintenanceTaskActivityConfiguration : IEntityTypeConfiguration<MaintenanceTaskActivity>
{
    public void Configure(EntityTypeBuilder<MaintenanceTaskActivity> builder)
    {
        builder.ToTable("maintenance_task_activities", "maintenance");

        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.Id).HasColumnName("id");

        builder.Property(activity => activity.MaintenanceTaskId).HasColumnName("maintenance_task_id").IsRequired();
        builder.HasIndex(activity => activity.MaintenanceTaskId);

        builder.Property(activity => activity.AssignmentId).HasColumnName("assignment_id");
        builder.HasIndex(activity => activity.AssignmentId);

        builder.Property(activity => activity.ActivityType)
            .HasColumnName("activity_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.HasIndex(activity => activity.ActivityType);

        builder.Property(activity => activity.FromStatus)
            .HasColumnName("from_status")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(activity => activity.ToStatus)
            .HasColumnName("to_status")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(activity => activity.Detail)
            .HasColumnName("detail")
            .HasColumnType("text");

        builder.Property(activity => activity.PerformedBy).HasColumnName("performed_by");

        builder.Property(activity => activity.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.HasIndex(activity => activity.CreatedAt);

        builder.HasOne(activity => activity.Assignment)
            .WithMany()
            .HasForeignKey(activity => activity.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
