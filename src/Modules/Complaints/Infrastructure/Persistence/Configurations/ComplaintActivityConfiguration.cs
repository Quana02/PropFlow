using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Complaints.Domain.ComplaintActivities;

namespace PropFlow.Modules.Complaints.Infrastructure.Persistence.Configurations;

public class ComplaintActivityConfiguration : IEntityTypeConfiguration<ComplaintActivity>
{
    public void Configure(EntityTypeBuilder<ComplaintActivity> builder)
    {
        builder.ToTable("complaint_activities", "complaints");

        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.Id).HasColumnName("id");

        builder.Property(activity => activity.ComplaintId).HasColumnName("complaint_id").IsRequired();
        builder.HasIndex(activity => activity.ComplaintId);

        builder.Property(activity => activity.FollowupId).HasColumnName("followup_id");
        builder.HasIndex(activity => activity.FollowupId);

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

        builder.HasOne(activity => activity.Followup)
            .WithMany()
            .HasForeignKey(activity => activity.FollowupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
