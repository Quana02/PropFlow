using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Complaints.Application;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

// Read-only adapter over master records. Complaint persists scalar references only.
public sealed class ComplaintContextSource(ApartmentsDbContext apartments, ServiceRequestsDbContext requests,
    PropertyAssetsDbContext assets) : IComplaintContextSource
{
    public async Task<bool> IsValidAsync(ComplaintContext c, CancellationToken ct)
    {
        if (c.ApartmentUnitId.HasValue && !await apartments.ApartmentUnits.AnyAsync(a => a.Id == c.ApartmentUnitId, ct)) return false;
        if (c.RelatedServiceRequestId.HasValue && !await requests.ServiceRequests.AnyAsync(r => r.Id == c.RelatedServiceRequestId, ct)) return false;
        if (c.FacilityId.HasValue && !await assets.Facilities.AnyAsync(f => f.Id == c.FacilityId, ct)) return false;
        if (c.EquipmentId.HasValue && !await assets.Equipment.AnyAsync(e => e.Id == c.EquipmentId &&
            (!c.FacilityId.HasValue || e.FacilityId == c.FacilityId), ct)) return false;
        return true;
    }

    public async Task<ComplaintContextOptions> OptionsAsync(string? search, CancellationToken ct)
    {
        var term = (search?.Trim() ?? "").ToLowerInvariant();
        // Bounded selector results, including historical Service Requests as complaint context.
        var units = await apartments.ApartmentUnits.AsNoTracking().Where(a => a.UnitNumber.ToLower().Contains(term))
            .OrderBy(a => a.UnitNumber).Take(50).Select(a => new ContextOption(a.Id, a.UnitNumber)).ToArrayAsync(ct);
        var serviceRequests = await requests.ServiceRequests.AsNoTracking().Where(r => r.RequestNumber.ToLower().Contains(term) || r.Title.ToLower().Contains(term))
            .OrderByDescending(r => r.SubmittedAt).Take(50).Select(r => new ContextOption(r.Id, r.RequestNumber + " — " + r.Title)).ToArrayAsync(ct);
        var facilities = await assets.Facilities.AsNoTracking().Where(f => f.Name.ToLower().Contains(term))
            .OrderBy(f => f.Name).Take(50).Select(f => new ContextOption(f.Id, f.Name)).ToArrayAsync(ct);
        var equipment = await assets.Equipment.AsNoTracking().Where(e => e.Name.ToLower().Contains(term))
            .OrderBy(e => e.Name).Take(50).Select(e => new ContextOption(e.Id, e.Name)).ToArrayAsync(ct);
        return new(units, serviceRequests, facilities, equipment);
    }
}
