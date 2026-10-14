using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestFeedbacks;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Configurations;

public sealed class ServiceRequestFeedbackConfiguration : IEntityTypeConfiguration<ServiceRequestFeedback>
{
    public void Configure(EntityTypeBuilder<ServiceRequestFeedback> builder)
    {
        builder.ToTable("service_request_feedbacks", "service_requests", table =>
            table.HasCheckConstraint("CK_service_request_feedbacks_rating", "rating BETWEEN 1 AND 5"));
        builder.HasKey(feedback => feedback.Id);
        builder.Property(feedback => feedback.Id).HasColumnName("id");
        builder.Property(feedback => feedback.ServiceRequestId).HasColumnName("service_request_id").IsRequired();
        builder.HasIndex(feedback => feedback.ServiceRequestId).IsUnique();
        builder.Property(feedback => feedback.ResidentId).HasColumnName("resident_id").IsRequired();
        builder.HasIndex(feedback => feedback.ResidentId);
        builder.Property(feedback => feedback.Rating).HasColumnName("rating").IsRequired();
        builder.Property(feedback => feedback.Comment).HasColumnName("comment").HasMaxLength(1000).IsRequired();
        builder.Property(feedback => feedback.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(feedback => feedback.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(feedback => feedback.ServiceRequest).WithOne()
            .HasForeignKey<ServiceRequestFeedback>(feedback => feedback.ServiceRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
