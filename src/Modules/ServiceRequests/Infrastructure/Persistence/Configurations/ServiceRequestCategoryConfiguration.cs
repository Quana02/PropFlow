using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence.Configurations;

public class ServiceRequestCategoryConfiguration : IEntityTypeConfiguration<ServiceRequestCategory>
{
    public void Configure(EntityTypeBuilder<ServiceRequestCategory> builder)
    {
        builder.ToTable("service_request_categories", "service_requests");

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).HasColumnName("id");

        builder.Property(category => category.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(category => category.Code).IsUnique();

        builder.Property(category => category.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(category => category.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(category => category.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(category => category.DisplayOrder)
            .HasColumnName("display_order")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(category => category.CreatedBy).HasColumnName("created_by");
        builder.Property(category => category.UpdatedBy).HasColumnName("updated_by");

        builder.Property(category => category.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(category => category.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasMany(category => category.ServiceRequests)
            .WithOne(request => request.Category)
            .HasForeignKey(request => request.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(category => category.ServiceRequests)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
