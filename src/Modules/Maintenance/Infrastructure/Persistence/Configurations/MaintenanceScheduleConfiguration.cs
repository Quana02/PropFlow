using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Configurations;

public class MaintenanceScheduleConfiguration : IEntityTypeConfiguration<MaintenanceSchedule>
{
    public void Configure(EntityTypeBuilder<MaintenanceSchedule> builder)
    {
        builder.ToTable("maintenance_schedules", "maintenance", table =>
        {
            table.HasCheckConstraint(
                "CK_maintenance_schedules_planned_end_at",
                "planned_end_at IS NULL OR planned_end_at >= planned_start_at");
        });

        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.Id).HasColumnName("id");

        builder.Property(schedule => schedule.ScheduleCode)
            .HasColumnName("schedule_code")
            .HasMaxLength(30)
            .IsRequired();
        builder.HasIndex(schedule => schedule.ScheduleCode).IsUnique();

        builder.Property(schedule => schedule.FacilityId).HasColumnName("facility_id");
        builder.HasIndex(schedule => schedule.FacilityId);

        builder.Property(schedule => schedule.EquipmentId).HasColumnName("equipment_id");
        builder.HasIndex(schedule => schedule.EquipmentId);

        builder.Property(schedule => schedule.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(schedule => schedule.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(schedule => schedule.PlannedStartAt)
            .HasColumnName("planned_start_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.HasIndex(schedule => schedule.PlannedStartAt);

        builder.Property(schedule => schedule.PlannedEndAt)
            .HasColumnName("planned_end_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(schedule => schedule.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(MaintenanceScheduleStatus.ACTIVE)
            .IsRequired();
        builder.HasIndex(schedule => schedule.Status);

        builder.Property(schedule => schedule.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(schedule => schedule.UpdatedBy).HasColumnName("updated_by");

        builder.Property(schedule => schedule.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(schedule => schedule.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
