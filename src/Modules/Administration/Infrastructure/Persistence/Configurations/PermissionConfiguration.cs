using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Administration.Domain.Permissions;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions", "administration");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.Code)
            .HasColumnName("code")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(p => p.Code)
            .IsUnique();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(p => p.Module)
            .HasColumnName("module")
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(p => p.Module);

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasMany(p => p.RolePermissions)
            .WithOne(rp => rp.Permission)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(p => p.RolePermissions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasData(FixedRbacSeed.Permissions.Select(item => new
        {
            item.Id,
            item.Code,
            item.Name,
            item.Module,
            item.Description,
            IsActive = true,
            CreatedAt = FixedRbacSeed.CreatedAt,
            UpdatedAt = FixedRbacSeed.CreatedAt
        }));
    }
}
