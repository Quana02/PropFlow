using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence;

public class ResidentsDbContext : DbContext
{
    public ResidentsDbContext(DbContextOptions<ResidentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<ResidentApartment> ResidentApartments => Set<ResidentApartment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("residents");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ResidentsDbContext).Assembly);
    }
}
