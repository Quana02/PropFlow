using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure;

public sealed class FacilityGuideService(PropertyAssetsDbContext db, ISharedFacilityCatalog catalog,
    IMaintenanceStaffDirectory staff, ICurrentBuildingTimeZone timezone, TimeProvider clock) : IFacilityGuideSource
{
    private static FacilityGuideView Map(SharedFacilityReference facility, FacilityGuide? guide, IReadOnlyList<MaintenanceStaffRecord> people) => new(
        facility.Id, facility.Name, facility.Type, facility.Location, facility.Status, guide?.Published ?? false,
        guide?.Description ?? "", guide?.Audience ?? "GENERAL", guide?.MinimumAge, guide?.MaximumAge,
        guide?.ChildPolicy ?? "GUARDIAN_REQUIRED", guide?.SupervisionNote ?? "", guide?.Rules ?? "",
        guide is null ? [] : JsonSerializer.Deserialize<FacilityOpening[]>(guide.HoursJson)!, guide?.ClosedDates ?? [],
        guide?.ResponsibleStaffId, people.SingleOrDefault(x => x.UserId == guide?.ResponsibleStaffId)?.DisplayName,
        guide?.ContactDetails ?? "", guide?.Token ?? Guid.Empty, guide?.UpdatedAt ?? facility.UpdatedAt);
    private async Task<FacilityGuideView[]> All(CancellationToken ct)
    {
        var guides = await db.FacilityGuides.AsNoTracking().ToArrayAsync(ct); var people = await staff.GetActiveStaffAsync(ct);
        return (await catalog.ListAsync(ct)).Select(facility => Map(facility, guides.SingleOrDefault(x => x.FacilityId == facility.Id), people)).ToArray();
    }
    public async Task<FacilityGuidesManagement> ManagementAsync(CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(await timezone.GetAsync(ct)));
        return new(await All(ct), (await staff.GetActiveStaffAsync(ct)).Select(x => new FacilityContactOption(x.UserId,x.DisplayName)).ToArray(), DateOnly.FromDateTime(local.DateTime));
    }
    public async Task SaveAsync(Guid facility, SaveFacilityGuide input, Guid actor, CancellationToken ct)
    {
        var reference = await catalog.FindAsync(facility, ct) ?? throw new FacilityGuideFailure(404,"facility_not_found","Không tìm thấy tiện ích.");
        if (input.ResponsibleStaffId is { } responsible && await staff.GetActiveStaffAsync(responsible,ct) is null)
            throw new FacilityGuideFailure(400,"facility_contact_invalid","Người phụ trách phải là Staff đang hoạt động.");
        if (input.Published && reference.Status != "ACTIVE") throw new FacilityGuideFailure(409,"facility_unavailable","Chỉ xuất bản tiện ích đang hoạt động.");
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsRelational()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"facility-guide:"+facility}, 8))",ct);
        var entity = await db.FacilityGuides.SingleOrDefaultAsync(x => x.FacilityId==facility,ct);
        if (entity is not null) await db.Entry(entity).ReloadAsync(ct);
        if (entity?.Token != input.ExpectedToken) throw new FacilityGuideFailure(409,"facility_guide_changed","Hồ sơ tiện ích đã thay đổi. Tải lại trước khi lưu.");
        var creating = entity is null; entity ??= new(facility); entity.Change(input,actor,clock.GetUtcNow());
        if (creating) db.FacilityGuides.Add(entity);
        await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
    }
    public async Task<FacilityGuideWindow> ReadAsync(CancellationToken ct)
    {
        var guides = (await All(ct)).Where(x => x.Published && x.Status=="ACTIVE" && x.ResponsibleName is not null)
            .Select(x => x with { ResponsibleStaffId=null }).OrderBy(x => x.FacilityId).ToArray();
        return new(guides,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(guides))));
    }
}
