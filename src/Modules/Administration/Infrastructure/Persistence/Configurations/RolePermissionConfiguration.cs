using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.Permissions;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions", "administration");

        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });

        builder.Property(rp => rp.RoleId)
            .HasColumnName("role_id");

        builder.Property(rp => rp.PermissionId)
            .HasColumnName("permission_id");

        builder.Property(rp => rp.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(rp => rp.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(rp => rp.PermissionId);

        builder.HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        var resident = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var staff = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var accountant = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var manager = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var admin = Guid.Parse("55555555-5555-4555-8555-555555555555");
        var permissions = FixedRbacSeed.Permissions.ToDictionary(item => item.Code, item => item.Id);

        builder.HasData(
            Seed(resident, permissions[SystemPermissionCodes.UseResidentServices]),
            Seed(staff, permissions[SystemPermissionCodes.PerformAssignedOperations]),
            Seed(accountant, permissions[SystemPermissionCodes.ManageFinance]),
            Seed(manager, permissions[SystemPermissionCodes.ManageOperations]),
            Seed(admin, permissions[SystemPermissionCodes.ManageInternalAccounts]),
            Seed(admin, permissions[SystemPermissionCodes.ViewAdministrationActivity]),
            Seed(admin, permissions[SystemPermissionCodes.ViewSystemOverview]));
    }

    private static object Seed(Guid roleId, Guid permissionId) => new
    {
        RoleId = roleId,
        PermissionId = permissionId,
        CreatedBy = (Guid?)null,
        CreatedAt = FixedRbacSeed.CreatedAt
    };
}
