using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Complaints.Application;
using PropFlow.Modules.Complaints.Domain.Complaints;
using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;

namespace PropFlow.Modules.Complaints.Infrastructure.Persistence;

public sealed class EfComplaintStore(ComplaintsDbContext db) : IComplaintStore
{
    public async Task<ComplaintPage> ListAsync(Guid? residentId, Guid? staffId, string? search, ComplaintStatus? status, int page, CancellationToken ct)
    {
        var query = db.Complaints.AsNoTracking();
        if (residentId.HasValue) query = query.Where(x => x.ResidentId == residentId);
        if (staffId.HasValue) query = query.Where(x => x.Followups.Any(f => f.StaffUserId == staffId));
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Normalize().ToLowerInvariant();
            query = query.Where(x => x.Subject.ToLower().Contains(term) || x.ComplaintNumber.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.SubmittedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * 20).Take(20)
            .Select(x => new ComplaintItem(x.Id, x.ComplaintNumber, x.Subject, x.Status.ToString(), x.SubmittedAt)).ToArrayAsync(ct);
        return new(items, total, page, 20);
    }

    public Task<Complaint?> FindAsync(Guid id, CancellationToken ct) => db.Complaints
        .Include(x => x.Activities).Include(x => x.Followups).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Complaint complaint) => db.Complaints.Add(complaint);
    public void Add(ComplaintActivity activity) => db.ComplaintActivities.Add(activity);
    public void Add(ComplaintFollowup followup) => db.ComplaintFollowups.Add(followup);
}
