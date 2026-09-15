using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Configurations;

public class MaintenanceAssignmentConfiguration : IEntityTypeConfiguration<MaintenanceAssignment>
{
    public void Configure(EntityTypeBuilder<MaintenanceAssignment> builder)
    {
        builder.ToTable("maintenance_assignments", "maintenance", table =>
        {
            table.HasCheckConstraint(
                "CK_maintenance_assignments_started_at",
                "started_at IS NULL OR started_at >= assigned_at");
            table.HasCheckConstraint(
                "CK_maintenance_assignments_completed_at",
                "started_at IS NULL OR completed_at IS NULL OR completed_at >= started_at");
            table.HasCheckConstraint(
                "CK_maintenance_assignments_ended_at",
                "ended_at IS NULL OR ended_at >= assigned_at");
        });

        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Id).HasColumnName("id");

        builder.Property(assignment => assignment.MaintenanceTaskId).HasColumnName("maintenance_task_id").IsRequired();
        builder.HasIndex(assignment => assignment.MaintenanceTaskId);
        builder.HasIndex(assignment => assignment.MaintenanceTaskId)
            .IsUnique()
            .HasFilter("\"status\" IN ('ASSIGNED', 'IN_PROGRESS')")
            .HasDatabaseName("IX_maintenance_assignments_active_maintenance_task_id");

        builder.Property(assignment => assignment.StaffUserId).HasColumnName("staff_user_id").IsRequired();
        builder.HasIndex(assignment => assignment.StaffUserId);

        builder.Property(assignment => assignment.AssignedBy).HasColumnName("assigned_by").IsRequired();

        builder.Property(assignment => assignment.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(AssignmentStatus.ASSIGNED)
            .IsRequired();
        builder.HasIndex(assignment => assignment.Status);

        builder.Property(assignment => assignment.AssignmentNote)
            .HasColumnName("assignment_note")
            .HasColumnType("text");

        builder.Property(assignment => assignment.AssignedAt)
            .HasColumnName("assigned_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.HasIndex(assignment => assignment.AssignedAt);

        builder.Property(assignment => assignment.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(assignment => assignment.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(assignment => assignment.EndedAt)
            .HasColumnName("ended_at")
            .HasColumnType("timestamp with time zone");

        builder.Ignore(assignment => assignment.IsActive);
    }
}
