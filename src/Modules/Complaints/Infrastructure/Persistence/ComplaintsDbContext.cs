using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;
using PropFlow.Modules.Complaints.Domain.Complaints;

namespace PropFlow.Modules.Complaints.Infrastructure.Persistence;

public class ComplaintsDbContext : DbContext
{
    public ComplaintsDbContext(DbContextOptions<ComplaintsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<ComplaintFollowup> ComplaintFollowups => Set<ComplaintFollowup>();
    public DbSet<ComplaintActivity> ComplaintActivities => Set<ComplaintActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("complaints");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ComplaintsDbContext).Assembly);
    }
}
