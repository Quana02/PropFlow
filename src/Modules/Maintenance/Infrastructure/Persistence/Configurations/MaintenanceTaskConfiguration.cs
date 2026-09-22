using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Configurations;

public class MaintenanceTaskConfiguration : IEntityTypeConfiguration<MaintenanceTask>
{
    public void Configure(EntityTypeBuilder<MaintenanceTask> builder)
    {
        builder.ToTable("maintenance_tasks", "maintenance", table =>
        {
            table.HasCheckConstraint(
                "CK_maintenance_tasks_due_at",
                "planned_start_at IS NULL OR due_at IS NULL OR due_at >= planned_start_at");
            table.HasCheckConstraint(
                "CK_maintenance_tasks_completed_at",
                "started_at IS NULL OR completed_at IS NULL OR completed_at >= started_at");
            table.HasCheckConstraint(
                "CK_maintenance_tasks_closed_at",
                "completed_at IS NULL OR closed_at IS NULL OR closed_at >= completed_at");
        });

        builder.HasKey(task => task.Id);
        builder.Property(task => task.Id).HasColumnName("id");

        builder.Property(task => task.TaskNumber)
            .HasColumnName("task_number")
            .HasMaxLength(30)
            .IsRequired();
        builder.HasIndex(task => task.TaskNumber).IsUnique();

        builder.Property(task => task.ScheduleId).HasColumnName("schedule_id");
        builder.HasIndex(task => task.ScheduleId);

        builder.Property(task => task.SourceServiceRequestId).HasColumnName("source_service_request_id");
        builder.HasIndex(task => task.SourceServiceRequestId);

        builder.Property(task => task.SourceComplaintId).HasColumnName("source_complaint_id");
        builder.HasIndex(task => task.SourceComplaintId);

        builder.Property(task => task.FacilityId).HasColumnName("facility_id");
        builder.Property(task => task.EquipmentId).HasColumnName("equipment_id");
        builder.HasIndex(task => task.EquipmentId);

        builder.Property(task => task.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(task => task.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(task => task.PriorityCode)
            .HasColumnName("priority_code")
            .HasMaxLength(30);
        builder.HasIndex(task => task.PriorityCode);

        builder.Property(task => task.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(MaintenanceTaskStatus.OPEN)
            .IsRequired();
        builder.HasIndex(task => task.Status);

        builder.Property(task => task.PlannedStartAt)
            .HasColumnName("planned_start_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(task => task.DueAt)
            .HasColumnName("due_at")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(task => task.DueAt);

        builder.Property(task => task.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(task => task.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(task => task.ClosedAt)
            .HasColumnName("closed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(task => task.ClosedBy).HasColumnName("closed_by");
        builder.Property(task => task.CreatedBy).HasColumnName("created_by").IsRequired();

        builder.Property(task => task.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(task => task.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(task => task.Schedule)
            .WithMany()
            .HasForeignKey(task => task.ScheduleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(task => task.Assignments)
            .WithOne(assignment => assignment.MaintenanceTask)
            .HasForeignKey(assignment => assignment.MaintenanceTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(task => task.Activities)
            .WithOne(activity => activity.MaintenanceTask)
            .HasForeignKey(activity => activity.MaintenanceTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(task => task.Results)
            .WithOne(result => result.MaintenanceTask)
            .HasForeignKey(result => result.MaintenanceTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(task => task.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(task => task.Activities).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(task => task.Results).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
