using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence;

public class ApartmentsDbContext : DbContext
{
    public ApartmentsDbContext(DbContextOptions<ApartmentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<ApartmentUnit> ApartmentUnits => Set<ApartmentUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("apartments");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApartmentsDbContext).Assembly);
    }
}
