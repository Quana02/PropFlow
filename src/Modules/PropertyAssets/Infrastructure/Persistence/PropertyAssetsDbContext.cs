using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using EquipmentEntity = PropFlow.Modules.PropertyAssets.Domain.Equipment.Equipment;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

public class PropertyAssetsDbContext : DbContext
{
    public PropertyAssetsDbContext(DbContextOptions<PropertyAssetsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<PropFlow.Shared.Messaging.OutboxEvent> ResidentEvents => Set<PropFlow.Shared.Messaging.OutboxEvent>();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) {
        AssetStatusEvents.Capture(this); return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) {
        AssetStatusEvents.Capture(this); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<FacilityType> FacilityTypes => Set<FacilityType>();
    public DbSet<EquipmentEntity> Equipment => Set<EquipmentEntity>();
    public DbSet<PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentType> EquipmentTypes => Set<PropFlow.Modules.PropertyAssets.Domain.Equipment.EquipmentType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("property_assets");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PropertyAssetsDbContext).Assembly);
    }
}
