using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Application;

public sealed class ResidentResidencyService(
    ResidentsDbContext db,
    IApartmentOverviewSource apartments,
    IApartmentResidentRelationshipSource apartmentRelationships,
    TimeProvider clock)
{
    public async Task<ResidentApartment> CreateAsync(Guid residentId, CreateResidencyCommand command, Guid? actorId, CancellationToken ct)
    {
        IDbContextTransaction? ownedTransaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await LockResidentAsync(residentId, ct);
            await ValidateCreateAsync(residentId, command, ct);
            var relation = NewRelation(residentId, command, actorId);
            db.ResidentApartments.Add(relation); await db.SaveChangesAsync(ct);
            if (ownedTransaction is not null) await ownedTransaction.CommitAsync(ct);
            return relation;
        }
        finally { if (ownedTransaction is not null) await ownedTransaction.DisposeAsync(); }
    }

    public async Task<ResidentApartment> MoveAsync(Guid residentId, Guid sourceResidencyId, CreateResidencyCommand target, Guid? actorId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockResidentAsync(residentId, ct);
        var source = await db.ResidentApartments.SingleOrDefaultAsync(x => x.Id == sourceResidencyId && x.ResidentId == residentId, ct)
            ?? throw new ResidentResidencyException(404, "residency_not_found", "Không tìm thấy quan hệ cư trú hiện tại.");
        if (source.Status != ResidencyStatus.ACTIVE || source.EndDate is not null)
            throw new ResidentResidencyException(409, "residency_not_active", "Quan hệ cư trú nguồn không còn hoạt động.");
        if (target.StartDate < source.StartDate)
            throw new ResidentResidencyException(400, "invalid_move_start_date", "Ngày bắt đầu tại căn hộ mới không được trước ngày bắt đầu của quan hệ cư trú nguồn.");
        if (source.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD && await db.ResidentApartments.AnyAsync(x => x.HouseholdHeadResidencyId == source.Id && x.Status == ResidencyStatus.ACTIVE && x.EndDate == null, ct))
            throw new ResidentResidencyException(409, "household_members_remain", "Hãy kết thúc hoặc chuyển chủ hộ cho các thành viên trước.");
        await ValidateCreateAsync(residentId, target, ct);
        source.EndResidency(target.StartDate, actorId, clock.GetUtcNow());
        var replacement = NewRelation(residentId, target, actorId);
        db.ResidentApartments.Add(replacement);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return replacement; }
        catch (ResidentResidencyException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new ResidentResidencyException(409, "residency_move_failed", "Không thể chuyển căn hộ. Quan hệ cư trú hiện tại chưa bị thay đổi.", ex); }
    }

    private async Task LockResidentAsync(Guid residentId, CancellationToken ct)
    {
        var resident = await db.Residents.FromSqlInterpolated($"SELECT * FROM residents.residents WHERE id = {residentId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (resident is null) throw new ResidentResidencyException(404, "resident_not_found", "Không tìm thấy hồ sơ cư dân.");
    }

    private async Task ValidateCreateAsync(Guid residentId, CreateResidencyCommand command, CancellationToken ct)
    {
        if (command.StartDate == default)
            throw new ResidentResidencyException(400, "start_date_required", "Ngày bắt đầu cư trú là bắt buộc.");
        if (command.EndDate.HasValue && command.EndDate.Value < command.StartDate)
            throw new ResidentResidencyException(400, "invalid_residency_dates", "Ngày kết thúc không được trước ngày bắt đầu cư trú.");
        if (!(await apartments.GetActiveApartmentIdsAsync(ct)).Contains(command.ApartmentUnitId)) throw new ResidentResidencyException(400, "invalid_apartment", "Căn hộ không hợp lệ hoặc không còn hoạt động.");
        if (await db.ResidentApartments.AnyAsync(a => a.ResidentId == residentId && a.ApartmentUnitId == command.ApartmentUnitId && a.Status == ResidencyStatus.ACTIVE && a.EndDate == null, ct))
            throw new ResidentResidencyException(409, "RESIDENT_ALREADY_ACTIVE_IN_APARTMENT", "Cư dân đã có quan hệ cư trú hiện hành tại căn hộ này.");
        if (command.ResidencyType == ResidencyType.OWNER_OCCUPIED &&
            !(await apartmentRelationships.GetCurrentOwnerResidentIdsAsync(command.ApartmentUnitId, command.StartDate, ct)).Contains(residentId))
            throw new ResidentResidencyException(409, "owner_occupied_requires_current_ownership", "Chỉ chủ sở hữu hiện tại của căn hộ mới được chọn hình thức chủ sở hữu đang cư trú.");
        await ValidateHouseholdAsync(residentId, command.ApartmentUnitId, command.HouseholdRole, command.HouseholdHeadResidencyId, command.RelationshipToHead, ct);
    }

    private ResidentApartment NewRelation(Guid residentId, CreateResidencyCommand command, Guid? actorId) =>
        new(residentId, command.ApartmentUnitId, command.HouseholdRole, command.ResidencyType, command.StartDate, clock.GetUtcNow(), command.HouseholdHeadResidencyId, command.RelationshipToHead, command.EndDate, command.Note, actorId);

    public async Task EndAsync(Guid residentId, Guid residencyId, DateOnly endDate, Guid? actorId, CancellationToken ct)
    {
        var relation = await db.ResidentApartments.SingleOrDefaultAsync(a => a.Id == residencyId && a.ResidentId == residentId, ct) ?? throw new ResidentResidencyException(404, "residency_not_found", "Không tìm thấy residency.");
        if (relation.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD && await db.ResidentApartments.AnyAsync(a => a.HouseholdHeadResidencyId == relation.Id && a.Status == ResidencyStatus.ACTIVE && a.EndDate == null, ct))
            throw new ResidentResidencyException(409, "household_members_remain", "Hãy kết thúc hoặc chuyển chủ hộ cho các thành viên trước.");
        relation.EndResidency(endDate, actorId, clock.GetUtcNow()); await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EligibleHouseholdHead>> EligibleHeadsAsync(Guid apartmentUnitId, DateOnly today, CancellationToken ct) =>
        await db.ResidentApartments.AsNoTracking().Where(a => a.ApartmentUnitId == apartmentUnitId && a.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD && a.Status == ResidencyStatus.ACTIVE && a.StartDate <= today && (a.EndDate == null || a.EndDate >= today))
            .Join(db.Residents.AsNoTracking(), a => a.ResidentId, r => r.Id, (a, r) => new { Residency = a, Resident = r })
            .OrderBy(x => x.Resident.FullName)
            .Select(x => new EligibleHouseholdHead(x.Residency.Id, x.Resident.Id, x.Resident.FullName, x.Resident.ResidentCode))
            .ToListAsync(ct);

    private async Task ValidateHouseholdAsync(Guid residentId, Guid apartmentUnitId, HouseholdRole role, Guid? headResidencyId, HouseholdRelationship? relationship, CancellationToken ct)
    {
        if (role == HouseholdRole.HOUSEHOLD_HEAD)
        {
            if (await db.ResidentApartments.AnyAsync(a => a.ApartmentUnitId == apartmentUnitId && a.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD && a.Status == ResidencyStatus.ACTIVE && a.EndDate == null, ct)) throw new ResidentResidencyException(409, "household_head_exists", "Căn hộ đã có chủ hộ đang cư trú.");
            return;
        }
        if (role != HouseholdRole.HOUSEHOLD_MEMBER) return;
        var head = await db.ResidentApartments.SingleOrDefaultAsync(a => a.Id == headResidencyId, ct);
        if (head is null || head.ResidentId == residentId || head.ApartmentUnitId != apartmentUnitId || head.HouseholdRole != HouseholdRole.HOUSEHOLD_HEAD || head.Status != ResidencyStatus.ACTIVE || head.EndDate != null)
            throw new ResidentResidencyException(400, "invalid_household_head", "Chủ hộ phải đang cư trú tại đúng căn hộ và không thể là chính cư dân này.");
        if (!relationship.HasValue) throw new ResidentResidencyException(400, "relationship_required", "Thành viên hộ phải có quan hệ với chủ hộ.");
    }
}
public sealed record CreateResidencyCommand(Guid ApartmentUnitId, HouseholdRole HouseholdRole, ResidencyType ResidencyType, Guid? HouseholdHeadResidencyId, HouseholdRelationship? RelationshipToHead, DateOnly StartDate, DateOnly? EndDate, string? Note);
public sealed record EligibleHouseholdHead(Guid ResidencyId, Guid ResidentId, string FullName, string ResidentCode);
public sealed class ResidentResidencyException(int statusCode, string code, string message, Exception? innerException = null) : Exception(message, innerException) { public int StatusCode { get; } = statusCode; public string Code { get; } = code; }
