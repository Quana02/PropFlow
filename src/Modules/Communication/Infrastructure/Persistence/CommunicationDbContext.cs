using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Communication.Domain.Notifications;
using PropFlow.Modules.Communication.Domain.SupportChat;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence;

public class CommunicationDbContext : DbContext
{
    public CommunicationDbContext(DbContextOptions<CommunicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<FinanceEmail> FinanceEmails => Set<FinanceEmail>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementVersion> AnnouncementVersions => Set<AnnouncementVersion>();
    public DbSet<AnnouncementAudience> AnnouncementAudiences => Set<AnnouncementAudience>();
    public DbSet<SupportConversation> SupportConversations => Set<SupportConversation>();
    public DbSet<PropFlow.Modules.Communication.Domain.EmergencyCalls.EmergencyCall> EmergencyCalls => Set<PropFlow.Modules.Communication.Domain.EmergencyCalls.EmergencyCall>();
    public DbSet<PropFlow.Modules.Communication.Domain.EmergencyCalls.EmergencyConnection> EmergencyConnections => Set<PropFlow.Modules.Communication.Domain.EmergencyCalls.EmergencyConnection>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<SupportManagerPresence> SupportManagerPresence => Set<SupportManagerPresence>();
    public DbSet<SupportManagerConnection> SupportManagerConnections => Set<SupportManagerConnection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("communication");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommunicationDbContext).Assembly);
    }
}
