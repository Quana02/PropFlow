using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Communication.Domain.Notifications;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence;

public class CommunicationDbContext : DbContext
{
    public CommunicationDbContext(DbContextOptions<CommunicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementVersion> AnnouncementVersions => Set<AnnouncementVersion>();
    public DbSet<AnnouncementAudience> AnnouncementAudiences => Set<AnnouncementAudience>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("communication");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommunicationDbContext).Assembly);
    }
}
