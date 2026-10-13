using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Configurations;

public sealed class MaintenanceResultAttachmentConfiguration : IEntityTypeConfiguration<MaintenanceResultAttachment>
{
    public void Configure(EntityTypeBuilder<MaintenanceResultAttachment> builder)
    {
        builder.ToTable("maintenance_result_attachments", "maintenance");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.MaintenanceResultId).HasColumnName("maintenance_result_id").IsRequired();
        builder.Property(item => item.StorageKey).HasColumnName("storage_key").HasMaxLength(512).IsRequired();
        builder.HasIndex(item => item.StorageKey).IsUnique();
        builder.Property(item => item.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(item => item.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(item => item.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(item => item.UploadedBy).HasColumnName("uploaded_by").IsRequired();
        builder.Property(item => item.UploadedAt).HasColumnName("uploaded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(item => item.MaintenanceResultId);
        builder.HasOne(item => item.MaintenanceResult).WithMany().HasForeignKey(item => item.MaintenanceResultId).OnDelete(DeleteBehavior.Restrict);
    }
}
