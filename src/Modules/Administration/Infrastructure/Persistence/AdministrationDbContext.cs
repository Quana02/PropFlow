using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Domain.AuditLogs;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.SystemConfigurations;
using PropFlow.Modules.Administration.Domain.UserAccessHistories;

using PropFlow.Modules.Administration.Domain.UserRoleAssignments;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence;

public class AdministrationDbContext : DbContext
{
    public AdministrationDbContext(DbContextOptions<AdministrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();

    public DbSet<UserAccessHistory> UserAccessHistories => Set<UserAccessHistory>();
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("administration");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdministrationDbContext).Assembly);
    }
}
