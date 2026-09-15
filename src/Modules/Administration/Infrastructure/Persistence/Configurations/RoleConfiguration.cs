using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Administration.Domain.Roles;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", "administration");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id");

        builder.Property(r => r.Code)
            .HasColumnName("code")
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(r => r.Code)
            .IsUnique();

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.Description)
            .HasColumnName("description")
            .HasColumnType("text");

        builder.Property(r => r.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasMany(r => r.RolePermissions)
            .WithOne(rp => rp.Role)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(r => r.RolePermissions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasData(
            new
            {
                Id = Guid.Parse("11111111-1111-4111-8111-111111111111"),
                Code = SystemRoleCodes.Resident,
                Name = "Resident",
                Description = "Resident user role",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero)
            },
            new
            {
                Id = Guid.Parse("22222222-2222-4222-8222-222222222222"),
                Code = SystemRoleCodes.Staff,
                Name = "Staff",
                Description = "Operational staff role",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero)
            },
            new
            {
                Id = Guid.Parse("33333333-3333-4333-8333-333333333333"),
                Code = SystemRoleCodes.Accountant,
                Name = "Accountant",
                Description = "Accounting and finance role",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero)
            },
            new
            {
                Id = Guid.Parse("44444444-4444-4444-8444-444444444444"),
                Code = SystemRoleCodes.Manager,
                Name = "Manager",
                Description = "Building management role",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero)
            },
            new
            {
                Id = Guid.Parse("55555555-5555-4555-8555-555555555555"),
                Code = SystemRoleCodes.Admin,
                Name = "Administrator",
                Description = "System administration role",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero)
            });
    }
}
