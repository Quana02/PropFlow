using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;

namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence;

public class MaintenanceDbContext(DbContextOptions<MaintenanceDbContext> options) : DbContext(options)
{
    public DbSet<PropFlow.Shared.Messaging.OutboxEvent> ResidentEvents => Set<PropFlow.Shared.Messaging.OutboxEvent>();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) {
        MaintenanceStatusEvents.Capture(this); return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) {
        MaintenanceStatusEvents.Capture(this); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    public DbSet<MaintenanceSchedule> MaintenanceSchedules => Set<MaintenanceSchedule>();
    public DbSet<MaintenanceTask> MaintenanceTasks => Set<MaintenanceTask>();
    public DbSet<MaintenanceAssignment> MaintenanceAssignments => Set<MaintenanceAssignment>();
    public DbSet<MaintenanceTaskActivity> MaintenanceTaskActivities => Set<MaintenanceTaskActivity>();
    public DbSet<MaintenanceResult> MaintenanceResults => Set<MaintenanceResult>();
    public DbSet<MaintenanceResultAttachment> MaintenanceResultAttachments => Set<MaintenanceResultAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("maintenance");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MaintenanceDbContext).Assembly);
    }
}
