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
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<EquipmentEntity> Equipment => Set<EquipmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("property_assets");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PropertyAssetsDbContext).Assembly);
    }
}
