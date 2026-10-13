using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Infrastructure;

public sealed class ResidentResidenceSource(ResidentsDbContext db) : IResidentResidenceSource
{
    public Task<ActiveResidentResidence?> FindActiveAsync(
        Guid userId,
        DateOnly date,
        CancellationToken ct) =>
        (from resident in db.Residents.AsNoTracking()
         join residence in db.ResidentApartments.AsNoTracking()
             on resident.Id equals residence.ResidentId
         where resident.UserId == userId
               && resident.Status == ResidentStatus.ACTIVE
               && residence.Status == ResidencyStatus.ACTIVE
               && residence.StartDate <= date
               && (!residence.EndDate.HasValue || residence.EndDate.Value >= date)
         orderby residence.StartDate descending, residence.CreatedAt descending
         select new ActiveResidentResidence(
             resident.Id,
             residence.Id,
             residence.ApartmentUnitId))
        .FirstOrDefaultAsync(ct);
}
