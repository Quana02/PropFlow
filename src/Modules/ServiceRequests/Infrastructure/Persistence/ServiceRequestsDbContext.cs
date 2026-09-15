using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

public class ServiceRequestsDbContext : DbContext
{
    public ServiceRequestsDbContext(DbContextOptions<ServiceRequestsDbContext> options)
        : base(options)
    {
    }

    public DbSet<ServiceRequestCategory> ServiceRequestCategories => Set<ServiceRequestCategory>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<ServiceRequestAssignment> ServiceRequestAssignments => Set<ServiceRequestAssignment>();
    public DbSet<ServiceRequestActivity> ServiceRequestActivities => Set<ServiceRequestActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("service_requests");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceRequestsDbContext).Assembly);
    }
}
