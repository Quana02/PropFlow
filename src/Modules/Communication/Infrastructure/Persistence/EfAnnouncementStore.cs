using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Communication.Application.Announcements;
using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence;

public sealed class EfAnnouncementStore(CommunicationDbContext db) : IAnnouncementStore
{
    private IQueryable<Announcement> Visible(ActiveResidentResidence? resident, bool manager)
    {
        var query = db.Announcements.AsNoTracking().Where(a => a.Status == AnnouncementStatus.PUBLISHED);
        if (manager) return query;
        if (resident is null) return query.Where(a => false);
        return query.Where(a => a.Audiences.Any(t => t.AudienceType == AnnouncementAudienceType.ALL_RESIDENTS ||
            t.AudienceType == AnnouncementAudienceType.ALL_USERS ||
            t.AudienceType == AnnouncementAudienceType.RESIDENT && t.ResidentId == resident.ResidentId ||
            t.AudienceType == AnnouncementAudienceType.APARTMENT && t.ApartmentUnitId == resident.ApartmentUnitId));
    }
    public async Task SaveAsync(Announcement announcement, CancellationToken ct)
    {
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync(ct);
    }
    public async Task<AnnouncementPage> ListAsync(ActiveResidentResidence? residence, bool manager, int page, CancellationToken ct)
    {
        var query = Visible(residence, manager);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.PublishedAt).ThenBy(a => a.Id).Skip((page - 1) * 20).Take(20)
            .Select(a => new AnnouncementView(a.Id, a.Title, a.Content, a.PublishedAt)).ToArrayAsync(ct);
        return new(items, total, page, 20);
    }
    public Task<AnnouncementView?> GetAsync(Guid id, ActiveResidentResidence? residence, bool manager, CancellationToken ct) =>
        Visible(residence, manager).Where(a => a.Id == id).Select(a => new AnnouncementView(a.Id, a.Title, a.Content, a.PublishedAt)).SingleOrDefaultAsync(ct);
}
