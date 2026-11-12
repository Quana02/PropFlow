using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Residents.Application;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Infrastructure;

public sealed class ResidentAccountScope(ResidentsDbContext db, IApartmentResidentRelationshipSource relationships,
    ResidentResidencyService residencyService)
    : IResidentAccountScope
{
    public async Task<ResidentAccountScopePage> SearchAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = db.Residents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(r => r.FullName.ToUpper().Contains(term) || r.ResidentCode.ToUpper().Contains(term)
                || (r.Email != null && r.Email.ToUpper().Contains(term)));
        }
        var total = await query.CountAsync(ct);
        pageIndex = Math.Clamp(pageIndex, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var rows = await query.OrderBy(r => r.FullName).ThenBy(r => r.Id)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize)
            .Select(r => new ResidentAccountScopeItem(r.Id, r.UserId, r.ResidentCode, r.FullName, r.Email,
                r.Status == ResidentStatus.ACTIVE, Array.Empty<string>())).ToListAsync(ct);
        if (rows.Count == 0) return new(rows, total, pageIndex);

        var today = await residencyService.BusinessDateAsync(ct);
        var ids = rows.Select(r => r.ResidentId).ToArray();
        var residencies = await db.ResidentApartments.AsNoTracking().Where(r => ids.Contains(r.ResidentId)
                && r.Status == ResidencyStatus.ACTIVE && r.StartDate <= today && (r.EndDate == null || r.EndDate >= today))
            .Select(r => new { r.ResidentId, r.ApartmentUnitId }).ToListAsync(ct);
        var apartments = (await relationships.GetApartmentsAsync(residencies.Select(r => r.ApartmentUnitId).Distinct().ToArray(), ct))
            .ToDictionary(a => a.Id, a => a.UnitNumber);
        var units = residencies.Where(r => apartments.ContainsKey(r.ApartmentUnitId))
            .Select(r => (r.ResidentId, Unit: apartments[r.ApartmentUnitId]))
            .Concat((await relationships.GetOwnershipsAsync(ids, ct)).Where(r => r.StartDate <= today && r.EndDate is null)
                .Select(r => (r.ResidentId, Unit: r.UnitNumber))).ToLookup(r => r.ResidentId, r => r.Unit);
        return new(rows.Select(r => r with { Apartments = units[r.ResidentId].Distinct().Order().ToArray() }).ToArray(), total, pageIndex);
    }

    public Task<Guid?> FindActiveAccountAsync(Guid residentId, CancellationToken ct) =>
        db.Residents.Where(r => r.Id == residentId && r.Status == ResidentStatus.ACTIVE)
            .Select(r => r.UserId).SingleOrDefaultAsync(ct);

    public async Task<bool> LockActiveLinkAsync(Guid residentId, Guid userId, CancellationToken ct) =>
        await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM residents.residents
            WHERE id = {residentId} AND user_id = {userId} AND status = 'ACTIVE' FOR UPDATE
            """).SingleOrDefaultAsync(ct) != Guid.Empty;
}
