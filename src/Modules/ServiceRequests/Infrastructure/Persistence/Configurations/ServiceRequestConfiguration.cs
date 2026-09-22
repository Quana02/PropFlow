using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Configurations;

public class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("service_requests", "service_requests");

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).HasColumnName("id");

        builder.Property(request => request.RequestNumber)
            .HasColumnName("request_number")
            .HasMaxLength(30)
            .IsRequired();
        builder.HasIndex(request => request.RequestNumber).IsUnique();

        builder.Property(request => request.ResidentId).HasColumnName("resident_id").IsRequired();
        builder.HasIndex(request => request.ResidentId);

        builder.Property(request => request.ResidentApartmentId).HasColumnName("resident_apartment_id").IsRequired();

        builder.Property(request => request.ApartmentUnitId).HasColumnName("apartment_unit_id");
        builder.HasIndex(request => request.ApartmentUnitId);

        builder.Property(request => request.CategoryId).HasColumnName("category_id");
        builder.HasIndex(request => request.CategoryId);

        builder.Property(request => request.FacilityId).HasColumnName("facility_id");
        builder.Property(request => request.EquipmentId).HasColumnName("equipment_id");

        builder.Property(request => request.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(request => request.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(request => request.FinalPriorityCode)
            .HasColumnName("final_priority_code")
            .HasMaxLength(30);

        builder.Property(request => request.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(ServiceRequestStatus.SUBMITTED)
            .IsRequired();
        builder.HasIndex(request => request.Status);
        builder.HasIndex(request => new { request.Status, request.FinalPriorityCode });

        builder.Property(request => request.SubmittedAt)
            .HasColumnName("submitted_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.HasIndex(request => request.SubmittedAt);

        builder.Property(request => request.ResolvedAt)
            .HasColumnName("resolved_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(request => request.ClosedAt)
            .HasColumnName("closed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(request => request.ClosedBy).HasColumnName("closed_by");

        builder.Property(request => request.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(request => request.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasMany(request => request.Assignments)
            .WithOne(assignment => assignment.ServiceRequest)
            .HasForeignKey(assignment => assignment.ServiceRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(request => request.Activities)
            .WithOne(activity => activity.ServiceRequest)
            .HasForeignKey(activity => activity.ServiceRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(request => request.Assignments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(request => request.Activities)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
