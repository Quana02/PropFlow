using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestEvidence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Configurations;

public sealed class ServiceRequestEvidenceFileConfiguration : IEntityTypeConfiguration<ServiceRequestEvidenceFile>
{
    public void Configure(EntityTypeBuilder<ServiceRequestEvidenceFile> builder)
    {
        builder.ToTable("service_request_evidence_files", "service_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ServiceRequestId).HasColumnName("service_request_id").IsRequired();
        builder.HasIndex(x => x.ServiceRequestId);
        builder.Property(x => x.ActivityId).HasColumnName("activity_id").IsRequired();
        builder.HasIndex(x => x.ActivityId);
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.Property(x => x.UploadedBy).HasColumnName("uploaded_by").IsRequired();
        builder.Property(x => x.UploadedAt).HasColumnName("uploaded_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<PropFlow.Modules.ServiceRequests.Domain.ServiceRequests.ServiceRequest>()
            .WithMany()
            .HasForeignKey(x => x.ServiceRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities.ServiceRequestActivity>()
            .WithMany()
            .HasForeignKey(x => x.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
