using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Complaints.Domain.Complaints;

namespace PropFlow.Modules.Complaints.Infrastructure.Persistence.Configurations;

public class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> builder)
    {
        builder.ToTable("complaints", "complaints");

        builder.HasKey(complaint => complaint.Id);
        builder.Property(complaint => complaint.Id).HasColumnName("id");

        builder.Property(complaint => complaint.ComplaintNumber)
            .HasColumnName("complaint_number")
            .HasMaxLength(30)
            .IsRequired();
        builder.HasIndex(complaint => complaint.ComplaintNumber).IsUnique();

        builder.Property(complaint => complaint.ResidentId).HasColumnName("resident_id").IsRequired();
        builder.HasIndex(complaint => complaint.ResidentId);

        builder.Property(complaint => complaint.ResidentApartmentId).HasColumnName("resident_apartment_id").IsRequired();

        builder.Property(complaint => complaint.ApartmentUnitId).HasColumnName("apartment_unit_id");
        builder.HasIndex(complaint => complaint.ApartmentUnitId);

        builder.Property(complaint => complaint.RelatedServiceRequestId).HasColumnName("related_service_request_id");
        builder.HasIndex(complaint => complaint.RelatedServiceRequestId);

        builder.Property(complaint => complaint.FacilityId).HasColumnName("facility_id");
        builder.Property(complaint => complaint.EquipmentId).HasColumnName("equipment_id");

        builder.Property(complaint => complaint.Subject)
            .HasColumnName("subject")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(complaint => complaint.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(complaint => complaint.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(ComplaintStatus.SUBMITTED)
            .IsRequired();
        builder.HasIndex(complaint => complaint.Status);

        builder.Property(complaint => complaint.OfficialResponse)
            .HasColumnName("official_response")
            .HasColumnType("text");

        builder.Property(complaint => complaint.SubmittedAt)
            .HasColumnName("submitted_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.HasIndex(complaint => complaint.SubmittedAt);

        builder.Property(complaint => complaint.ResolvedAt)
            .HasColumnName("resolved_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(complaint => complaint.ClosedAt)
            .HasColumnName("closed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(complaint => complaint.ClosedBy).HasColumnName("closed_by");

        builder.Property(complaint => complaint.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(complaint => complaint.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasMany(complaint => complaint.Followups)
            .WithOne(followup => followup.Complaint)
            .HasForeignKey(followup => followup.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(complaint => complaint.Activities)
            .WithOne(activity => activity.Complaint)
            .HasForeignKey(activity => activity.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(complaint => complaint.Followups)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(complaint => complaint.Activities)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
