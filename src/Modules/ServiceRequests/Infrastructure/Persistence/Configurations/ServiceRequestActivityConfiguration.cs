using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Configurations;

public class ServiceRequestActivityConfiguration : IEntityTypeConfiguration<ServiceRequestActivity>
{
    public void Configure(EntityTypeBuilder<ServiceRequestActivity> builder)
    {
        builder.ToTable("service_request_activities", "service_requests");

        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.Id).HasColumnName("id");

        builder.Property(activity => activity.ServiceRequestId).HasColumnName("service_request_id").IsRequired();
        builder.HasIndex(activity => activity.ServiceRequestId);

        builder.Property(activity => activity.AssignmentId).HasColumnName("assignment_id");
        builder.HasIndex(activity => activity.AssignmentId);

        builder.Property(activity => activity.ActivityType)
            .HasColumnName("activity_type")
            .HasConversion<string>()
            .HasMaxLength(50)
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

        builder.Property(activity => activity.Title)
            .HasColumnName("title")
            .HasMaxLength(200);

        builder.Property(activity => activity.Detail)
            .HasColumnName("detail")
            .HasColumnType("text");

        builder.Property(activity => activity.WorkResult)
            .HasColumnName("work_result")
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
