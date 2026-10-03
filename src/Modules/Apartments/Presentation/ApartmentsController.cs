using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Application;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Apartments.Presentation;

public static class ApartmentsAuthorizationPolicies { public const string Read = "apartments.read"; public const string Manage = "apartments.manage"; }

[ApiController, Route("api/v1/apartments")]
public sealed class ApartmentsController(ApartmentsDbContext db, IResidentApartmentReadSource residents,
    IApartmentOwnershipCommand ownerships, IApartmentStatusCommand statuses,
    ICurrentBuildingTimeZone buildingTimeZone, TimeProvider clock) : ControllerBase
{
    [HttpGet, Authorize(Policy = ApartmentsAuthorizationPolicies.Read)]
    public async Task<ActionResult<PagedApartmentsResponse>> List(string? search, MasterDataStatus? status,
        int? floorNumber, Guid? apartmentUnitTypeId, ApartmentOccupancyFilter? occupancy,
        int pageIndex = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var today = await TodayAsync(ct);
        var query = db.ApartmentUnits.AsNoTracking().Include(x => x.ApartmentUnitType).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToUpper(); query = query.Where(x => x.UnitNumber.ToUpper().Contains(term)); }
        if (status is not null) query = query.Where(x => x.Status == status);
        if (floorNumber.HasValue) query = query.Where(x => x.FloorNumber == floorNumber.Value);
        if (apartmentUnitTypeId.HasValue) query = query.Where(x => x.ApartmentUnitTypeId == apartmentUnitTypeId.Value);

        var activeApartmentIds = await residents.GetActiveApartmentIdsAsync(today, ct);
        if (occupancy == ApartmentOccupancyFilter.OCCUPIED) query = query.Where(x => activeApartmentIds.Contains(x.Id));
        if (occupancy == ApartmentOccupancyFilter.VACANT) query = query.Where(x => !activeApartmentIds.Contains(x.Id));

        pageIndex = Math.Max(pageIndex, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var occupiedCount = await query.CountAsync(x => activeApartmentIds.Contains(x.Id), ct);
        var inactiveCount = await query.CountAsync(x => x.Status == MasterDataStatus.INACTIVE, ct);
        var units = await query.OrderBy(x => x.FloorNumber).ThenBy(x => x.UnitNumber)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = units.Select(x => x.Id).ToArray();
        var currentOwnerships = await db.ApartmentOwnerships.AsNoTracking()
            .Where(x => ids.Contains(x.ApartmentUnitId) && x.EndDate == null).ToListAsync(ct);
        var ownerRecords = await residents.GetResidentsAsync(currentOwnerships.Select(x => x.OwnerResidentId).ToArray(), ct);
        var items = units.Select(x =>
        {
            var owners = currentOwnerships.Where(o => o.ApartmentUnitId == x.Id).Select(o => ownerRecords.GetValueOrDefault(o.OwnerResidentId))
                .Where(o => o is not null).Cast<ResidentLookupItem>().ToList();
            return new ApartmentListItem(x.Id, x.UnitNumber, x.FloorNumber, x.ApartmentUnitTypeId, x.ApartmentUnitType.Name, x.UsableAreaM2,
                x.Status.ToString(), owners, activeApartmentIds.Contains(x.Id));
        }).ToList();
        return Ok(new PagedApartmentsResponse(items, total, pageIndex, pageSize,
            new ApartmentSummary(total, occupiedCount, total - occupiedCount, inactiveCount)));
    }

    [HttpGet("me"), Authorize(Roles = "RESIDENT")]
    public async Task<ActionResult<IReadOnlyList<ResidentApartmentSelfItem>>> Me(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId)) return Forbid();
        var today = await TodayAsync(ct);
        var apartmentIds = await residents.GetActiveApartmentIdsForUserAsync(userId, today, ct);
        if (apartmentIds.Count == 0) return Ok(Array.Empty<ResidentApartmentSelfItem>());
        var units = await db.ApartmentUnits.AsNoTracking().Include(x => x.ApartmentUnitType).Where(x => apartmentIds.Contains(x.Id))
            .OrderBy(x => x.FloorNumber).ThenBy(x => x.UnitNumber).ToListAsync(ct);
        return Ok(units.Select(x => new ResidentApartmentSelfItem(x.Id, x.UnitNumber, x.FloorNumber, x.ApartmentUnitTypeId, x.ApartmentUnitType.Name,
            x.UsableAreaM2, x.BedroomCount, x.BathroomCount, x.HandoverDate, x.Status.ToString())).ToArray());
    }

    [HttpGet("owner-candidates"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<IReadOnlyList<ResidentLookupItem>> OwnerCandidates(Guid apartmentId, string? search, CancellationToken ct)
    {
        var current = await db.ApartmentOwnerships.Where(x => x.ApartmentUnitId == apartmentId && x.EndDate == null)
            .Select(x => x.OwnerResidentId).ToArrayAsync(ct);
        return (await residents.SearchResidentsAsync(search, 25, ct)).Where(x => !current.Contains(x.ResidentId)).ToList();
    }

    [HttpGet("ownership-candidates"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<IReadOnlyList<ApartmentOwnershipCandidateResponse>>> OwnershipCandidates(
        Guid ownerResidentId, string? search, CancellationToken ct)
    {
        if (ownerResidentId == Guid.Empty || !await residents.ResidentExistsAsync(ownerResidentId, ct))
            return BadRequest(Problem("owner_resident_invalid", "Không tìm thấy hồ sơ cư dân được chọn."));

        var currentlyOwnedIds = db.ApartmentOwnerships.AsNoTracking()
            .Where(x => x.OwnerResidentId == ownerResidentId && x.EndDate == null)
            .Select(x => x.ApartmentUnitId);
        var query = db.ApartmentUnits.AsNoTracking().Include(x => x.ApartmentUnitType)
            .Where(x => x.Status == MasterDataStatus.ACTIVE && !currentlyOwnedIds.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpper();
            query = query.Where(x => x.UnitNumber.ToUpper().Contains(term));
        }

        var candidates = await query.OrderBy(x => x.FloorNumber).ThenBy(x => x.UnitNumber).Take(50)
            .Select(x => new ApartmentOwnershipCandidateResponse(
                x.Id, x.UnitNumber, x.FloorNumber, x.ApartmentUnitType.Name, x.Status.ToString()))
            .ToListAsync(ct);
        return Ok(candidates);
    }

    [HttpGet("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Read)]
    public async Task<ActionResult<ApartmentDetailResponse>> Get(Guid id, CancellationToken ct)
    {
        var apartment = await db.ApartmentUnits.AsNoTracking().Include(x => x.ApartmentUnitType).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (apartment is null) return NotFound();
        var today = await TodayAsync(ct);
        var ownershipRows = await db.ApartmentOwnerships.AsNoTracking().Where(x => x.ApartmentUnitId == id).ToListAsync(ct);
        var ownerRecords = await residents.GetResidentsAsync(ownershipRows.Select(x => x.OwnerResidentId).ToArray(), ct);
        var currentOwners = ownershipRows.Where(x => x.EndDate == null)
            .Select(x => new ApartmentOwnerItem(x.Id, x.OwnerResidentId, ownerRecords.GetValueOrDefault(x.OwnerResidentId), x.StartDate, x.EndDate)).ToArray();
        var history = ownershipRows.OrderByDescending(x => x.StartDate)
            .Select(x => new ApartmentOwnershipHistoryItem(x.Id, x.OwnerResidentId, ownerRecords.GetValueOrDefault(x.OwnerResidentId), x.StartDate, x.EndDate)).ToArray();
        var associations = (await residents.GetAssociationsAsync([id], ct)).GetValueOrDefault(id) ?? [];
        var currentResidents = associations.Where(x => x.ResidencyStatus == "ACTIVE" && x.StartDate <= today && (x.EndDate == null || x.EndDate >= today)).ToArray();
        var residentHistory = associations.Where(x => !currentResidents.Any(current => current.ResidencyId == x.ResidencyId)).ToArray();
        return Ok(new ApartmentDetailResponse(apartment.Id, apartment.UnitNumber, apartment.FloorNumber, apartment.ApartmentUnitTypeId, apartment.ApartmentUnitType.Name,
            apartment.UsableAreaM2, apartment.BedroomCount, apartment.BathroomCount, apartment.HandoverDate,
            apartment.Description, apartment.Status.ToString(), currentOwners, history, currentResidents, residentHistory));
    }

    [HttpGet("{id:guid}/ownership-history"), Authorize(Policy = ApartmentsAuthorizationPolicies.Read)]
    public async Task<ActionResult<IReadOnlyList<ApartmentOwnershipHistoryItem>>> OwnershipHistory(Guid id, CancellationToken ct)
    {
        if (!await db.ApartmentUnits.AnyAsync(x => x.Id == id, ct)) return NotFound();
        var records = await db.ApartmentOwnerships.AsNoTracking().Where(x => x.ApartmentUnitId == id)
            .OrderBy(x => x.EndDate != null).ThenByDescending(x => x.EndDate).ThenByDescending(x => x.StartDate).ToListAsync(ct);
        var people = await residents.GetResidentsAsync(records.Select(x => x.OwnerResidentId).ToArray(), ct);
        return Ok(records.Select(x => new ApartmentOwnershipHistoryItem(x.Id, x.OwnerResidentId,
            people.GetValueOrDefault(x.OwnerResidentId), x.StartDate, x.EndDate)).ToList());
    }

    [HttpPost, Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<ApartmentDetailResponse>> Create(CreateApartmentRequest request, CancellationToken ct)
    {
        var validation = Validate(request.UnitNumber, request.FloorNumber, request.ApartmentUnitTypeId, request.UsableAreaM2, request.BedroomCount, request.BathroomCount);
        if (validation is not null) return validation;
        var apartmentType = await db.ApartmentUnitTypes.SingleOrDefaultAsync(x => x.Id == request.ApartmentUnitTypeId, ct);
        if (apartmentType is null) return BadRequest(Problem("APARTMENT_UNIT_TYPE_NOT_FOUND", "Loại căn hộ không tồn tại."));
        if (!Enum.TryParse<MasterDataStatus>(request.Status, true, out var status))
            return BadRequest(Problem("APARTMENT_STATUS_INVALID", "Trạng thái căn hộ không hợp lệ."));
        if (await db.ApartmentUnits.AnyAsync(x => x.UnitNumber == request.UnitNumber.Trim(), ct)) return Conflict(Problem("APARTMENT_UNIT_NUMBER_DUPLICATE", "Mã căn hộ đã tồn tại."));
        var apartment = new ApartmentUnit(request.UnitNumber, request.FloorNumber, clock.GetUtcNow(), request.ApartmentUnitTypeId,
            request.UsableAreaM2, request.BedroomCount, request.BathroomCount, request.HandoverDate, request.Description, Actor());
        if (status == MasterDataStatus.INACTIVE) apartment.Deactivate(Actor(), clock.GetUtcNow());
        db.ApartmentUnits.Add(apartment); await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = apartment.Id }, new ApartmentDetailResponse(apartment.Id,
            apartment.UnitNumber, apartment.FloorNumber, apartment.ApartmentUnitTypeId, apartmentType.Name, apartment.UsableAreaM2, apartment.BedroomCount,
            apartment.BathroomCount, apartment.HandoverDate, apartment.Description, apartment.Status.ToString(), [], [], [], []));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> Update(Guid id, UpdateApartmentRequest request, CancellationToken ct)
    {
        var validation = Validate(request.UnitNumber, request.FloorNumber, request.ApartmentUnitTypeId, request.UsableAreaM2, request.BedroomCount, request.BathroomCount);
        if (validation is not null) return validation;
        if (!await db.ApartmentUnitTypes.AnyAsync(x => x.Id == request.ApartmentUnitTypeId, ct)) return BadRequest(Problem("APARTMENT_UNIT_TYPE_NOT_FOUND", "Loại căn hộ không tồn tại."));
        var apartment = await db.ApartmentUnits.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (apartment is null) return NotFound();
        if (await db.ApartmentUnits.AnyAsync(x => x.Id != id && x.UnitNumber == request.UnitNumber.Trim(), ct)) return Conflict(Problem("APARTMENT_UNIT_NUMBER_DUPLICATE", "Mã căn hộ đã tồn tại."));
        apartment.Update(request.UnitNumber, request.FloorNumber, request.ApartmentUnitTypeId, request.UsableAreaM2,
            request.BedroomCount, request.BathroomCount, request.HandoverDate, request.Description, Actor(), clock.GetUtcNow());
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("{id:guid}/owners"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> AddOwner(Guid id, ChangeApartmentOwnerRequest request, CancellationToken ct)
    {
        try { await ownerships.AddOwnerAsync(id, request.OwnerResidentId, request.EffectiveDate ?? await TodayAsync(ct), Actor(), ct); return NoContent(); }
        catch (ApartmentOwnershipException exception) { return OwnershipProblem(exception); }
    }

    [HttpPatch("{id:guid}/owners/{ownershipId:guid}/end"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> EndOwner(Guid id, Guid ownershipId, EndApartmentOwnershipRequest request, CancellationToken ct)
    {
        try { await ownerships.EndOwnershipAsync(id, ownershipId, request.EndDate ?? await TodayAsync(ct), Actor(), ct); return NoContent(); }
        catch (ApartmentOwnershipException exception) { return OwnershipProblem(exception); }
    }

    [HttpPatch("{id:guid}/status"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> Status(Guid id, SetApartmentStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<MasterDataStatus>(request.Status, true, out var targetStatus)) return BadRequest(Problem("APARTMENT_STATUS_INVALID", "Trạng thái căn hộ không hợp lệ."));
        try
        {
            await statuses.SetAsync(id, targetStatus, await TodayAsync(ct), Actor(), ct);
            return NoContent();
        }
        catch (ApartmentStatusException exception)
        {
            return StatusCode(exception.StatusCode, new ProblemDetails
            {
                Title = "Không thể cập nhật trạng thái căn hộ",
                Detail = exception.Message,
                Status = exception.StatusCode,
                Extensions = { ["code"] = exception.Code }
            });
        }
    }

    private ActionResult? Validate(string unitNumber, int floorNumber, Guid apartmentUnitTypeId, decimal usableAreaM2, int? bedroomCount, int? bathroomCount)
    {
        if (string.IsNullOrWhiteSpace(unitNumber) || unitNumber.Trim().Length > 30) return BadRequest(Problem("APARTMENT_UNIT_NUMBER_REQUIRED", "Mã căn hộ là bắt buộc và tối đa 30 ký tự."));
        if (floorNumber < 0) return BadRequest(Problem("APARTMENT_FLOOR_NUMBER_INVALID", "Tầng phải lớn hơn hoặc bằng 0."));
        if (apartmentUnitTypeId == Guid.Empty) return BadRequest(Problem("APARTMENT_UNIT_TYPE_REQUIRED", "Loại căn hộ là bắt buộc."));
        if (usableAreaM2 <= 0) return BadRequest(Problem("APARTMENT_USABLE_AREA_INVALID", "Diện tích sử dụng phải lớn hơn 0."));
        if (bedroomCount is < 0 || bathroomCount is < 0) return BadRequest(Problem("APARTMENT_ROOM_COUNT_INVALID", "Số phòng ngủ và phòng tắm phải lớn hơn hoặc bằng 0."));
        return null;
    }

    private ProblemDetails Problem(string code, string detail) => new() { Title = "Dữ liệu căn hộ không hợp lệ", Detail = detail, Status = 400, Extensions = { ["code"] = code } };
    private IActionResult OwnershipProblem(ApartmentOwnershipException exception) => StatusCode(exception.StatusCode,
        new ProblemDetails { Title = "Không thể cập nhật quyền sở hữu", Detail = exception.Message, Status = exception.StatusCode, Extensions = { ["code"] = exception.Code } });
    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await buildingTimeZone.GetAsync(ct));
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
    }
    private Guid? Actor() => Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;
}

public enum ApartmentOccupancyFilter { OCCUPIED, VACANT }
public sealed record ApartmentSummary(int TotalCount, int OccupiedCount, int VacantCount, int InactiveCount);
public sealed record PagedApartmentsResponse(IReadOnlyList<ApartmentListItem> Items, int TotalCount, int PageIndex, int PageSize, ApartmentSummary Summary);
public sealed record ApartmentListItem(Guid Id, string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, string UnitType, decimal UsableAreaM2, string Status, IReadOnlyList<ResidentLookupItem> Owners, bool IsOccupied);
public sealed record ApartmentDetailResponse(Guid Id, string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, string UnitType, decimal UsableAreaM2,
    int? BedroomCount, int? BathroomCount, DateOnly? HandoverDate, string? Description, string Status,
    IReadOnlyList<ApartmentOwnerItem> Owners, IReadOnlyList<ApartmentOwnershipHistoryItem> OwnershipHistory,
    IReadOnlyList<ResidentAssociationItem> CurrentResidents, IReadOnlyList<ResidentAssociationItem> ResidentHistory)
{
    public IReadOnlyList<ActiveResidentAssociation> Occupants => CurrentResidents.Select(x =>
        new ActiveResidentAssociation(x.ResidentId, x.ResidentCode, x.FullName, x.HouseholdRole, x.ResidencyType,
            x.RelationshipToHead, x.StartDate, x.ResidencyStatus)).ToArray();
}
public sealed record ResidentApartmentSelfItem(Guid Id, string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, string UnitType, decimal UsableAreaM2,
    int? BedroomCount, int? BathroomCount, DateOnly? HandoverDate, string Status);
public sealed record ApartmentOwnerItem(Guid OwnershipId, Guid ResidentId, ResidentLookupItem? Owner, DateOnly StartDate, DateOnly? EndDate);
public sealed record ApartmentOwnershipCandidateResponse(Guid Id, string UnitNumber, int FloorNumber, string UnitType, string Status);
public sealed record ApartmentOwnershipHistoryItem(Guid OwnershipId, Guid OwnerResidentId, ResidentLookupItem? Owner, DateOnly StartDate, DateOnly? EndDate);
public sealed record CreateApartmentRequest(string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, decimal UsableAreaM2, int? BedroomCount, int? BathroomCount, DateOnly? HandoverDate, string? Description, string Status = "ACTIVE");
public sealed record UpdateApartmentRequest(string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, decimal UsableAreaM2, int? BedroomCount, int? BathroomCount, DateOnly? HandoverDate, string? Description);
public sealed record ChangeApartmentOwnerRequest(Guid OwnerResidentId, DateOnly? EffectiveDate);
public sealed record EndApartmentOwnershipRequest(DateOnly? EndDate);
public sealed record SetApartmentStatusRequest(string Status);
