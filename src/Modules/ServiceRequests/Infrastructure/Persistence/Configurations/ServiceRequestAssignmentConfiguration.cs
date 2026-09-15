using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Configurations;

public class ServiceRequestAssignmentConfiguration : IEntityTypeConfiguration<ServiceRequestAssignment>
{
    public void Configure(EntityTypeBuilder<ServiceRequestAssignment> builder)
    {
        builder.ToTable("service_request_assignments", "service_requests");

        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Id).HasColumnName("id");

        builder.Property(assignment => assignment.ServiceRequestId).HasColumnName("service_request_id").IsRequired();
        builder.HasIndex(assignment => assignment.ServiceRequestId)
            .HasDatabaseName("IX_service_request_assignments_service_request_id");

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
        builder.HasIndex(assignment => assignment.ServiceRequestId)
            .IsUnique()
            .HasFilter("\"status\" IN ('ASSIGNED', 'IN_PROGRESS')")
            .HasDatabaseName("IX_service_request_assignments_active_service_request_id");

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
