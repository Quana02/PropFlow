using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;

namespace PropFlow.Modules.Complaints.Infrastructure.Persistence.Configurations;

public class ComplaintFollowupConfiguration : IEntityTypeConfiguration<ComplaintFollowup>
{
    public void Configure(EntityTypeBuilder<ComplaintFollowup> builder)
    {
        builder.ToTable("complaint_followups", "complaints");

        builder.HasKey(followup => followup.Id);
        builder.Property(followup => followup.Id).HasColumnName("id");

        builder.Property(followup => followup.ComplaintId).HasColumnName("complaint_id").IsRequired();
        builder.HasIndex(followup => followup.ComplaintId);

        builder.Property(followup => followup.StaffUserId).HasColumnName("staff_user_id").IsRequired();
        builder.HasIndex(followup => followup.StaffUserId);

        builder.Property(followup => followup.AssignedBy).HasColumnName("assigned_by").IsRequired();

        builder.Property(followup => followup.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(followup => followup.Instruction)
            .HasColumnName("instruction")
            .HasColumnType("text");

        builder.Property(followup => followup.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(ComplaintFollowupStatus.ASSIGNED)
            .IsRequired();
        builder.HasIndex(followup => followup.Status);

        builder.Property(followup => followup.DueAt)
            .HasColumnName("due_at")
            .HasColumnType("timestamp with time zone");
        builder.HasIndex(followup => followup.DueAt);

        builder.Property(followup => followup.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(followup => followup.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(followup => followup.Result)
            .HasColumnName("result")
            .HasColumnType("text");

        builder.Property(followup => followup.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(followup => followup.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(followup => followup.IsActive);
    }
}
