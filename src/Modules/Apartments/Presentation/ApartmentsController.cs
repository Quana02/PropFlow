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
public sealed partial class ApartmentsController(ApartmentsDbContext db, IResidentApartmentReadSource residents,
    IApartmentOwnershipCommand ownerships, IApartmentStatusCommand statuses,
    ICurrentBuildingTimeZone buildingTimeZone, TimeProvider clock, ICurrentBuildingFloorLimit floorLimit) : ControllerBase
{
    [HttpGet, Authorize(Policy = ApartmentsAuthorizationPolicies.Read)]
    public async Task<ActionResult<PagedApartmentsResponse>> List(string? search, MasterDataStatus? status,
        int? floorNumber, Guid? apartmentUnitTypeId, ApartmentOccupancyFilter? occupancy,
        int pageIndex = 1, int pageSize = 20, CancellationToken ct = default,
        [FromQuery] bool? hasOwner = null, [FromQuery] decimal? minArea = null, [FromQuery] decimal? maxArea = null,
        [FromQuery] MasterDataStatus[]? statuses = null, [FromQuery] int[]? floorNumbers = null,
        [FromQuery] Guid[]? apartmentUnitTypeIds = null)
    {
        if(search?.Any(c=>char.IsControl(c)&&!char.IsWhiteSpace(c))==true||search?.Length>200||search?.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Length>12||floorNumber<0||minArea<0||maxArea<0||(minArea.HasValue&&maxArea.HasValue&&minArea>maxArea)
            ||(status.HasValue&&!Enum.IsDefined(status.Value))||(occupancy.HasValue&&!Enum.IsDefined(occupancy.Value)))
            return BadRequest(new ProblemDetails{Status=400,Title="Bộ lọc không hợp lệ. Tìm tối đa 200 ký tự, 12 từ; tầng và diện tích không âm; khoảng diện tích từ–đến phải hợp lệ."});
        var selectedStatuses=ApartmentDirectorySelection.Merge(status,statuses);
        var selectedFloors=ApartmentDirectorySelection.Merge(floorNumber,floorNumbers);
        var selectedTypes=ApartmentDirectorySelection.Merge(apartmentUnitTypeId,apartmentUnitTypeIds);
        if(statuses?.Length>100||floorNumbers?.Length>100||apartmentUnitTypeIds?.Length>100
            ||selectedStatuses.Length>100||selectedFloors.Length>100||selectedTypes.Length>100
            ||selectedStatuses.Any(x=>!Enum.IsDefined(x))||selectedFloors.Any(x=>x<0)
            ||apartmentUnitTypeIds?.Contains(Guid.Empty)==true)
            return BadRequest(new ProblemDetails{Status=400,Title="Bộ lọc căn hộ không hợp lệ. Chọn tối đa 100 giá trị hợp lệ mỗi trường."});
        var today = await TodayAsync(ct);
        var query = db.ApartmentUnits.AsNoTracking().Include(x => x.ApartmentUnitType).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            foreach(var word in search.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))
            {
                var term=word.ToUpperInvariant();var people=await residents.SearchApartmentPeopleAsync(term,today,ct);
                query=query.Where(x=>x.UnitNumber.ToUpper().Contains(term)||x.ApartmentUnitType.Name.ToUpper().Contains(term)
                    ||people.OccupiedApartmentIds.Contains(x.Id)||db.ApartmentOwnerships.Any(o=>o.ApartmentUnitId==x.Id&&o.StartDate<=today&&o.EndDate==null&&people.ResidentIds.Contains(o.OwnerResidentId)));
            }
        }
        if (selectedStatuses.Length>0) query = query.Where(x => selectedStatuses.Contains(x.Status));
        if (selectedFloors.Length>0) query = query.Where(x => selectedFloors.Contains(x.FloorNumber));
        if (selectedTypes.Length>0) query = query.Where(x => selectedTypes.Contains(x.ApartmentUnitTypeId));
        if(minArea.HasValue)query=query.Where(x=>x.UsableAreaM2>=minArea.Value);
        if(maxArea.HasValue)query=query.Where(x=>x.UsableAreaM2<=maxArea.Value);
        if(hasOwner.HasValue)query=query.Where(x=>db.ApartmentOwnerships.Any(o=>o.ApartmentUnitId==x.Id&&o.StartDate<=today&&o.EndDate==null)==hasOwner.Value);

        var activeApartmentIds = await residents.GetActiveApartmentIdsAsync(today, ct);
        if (occupancy == ApartmentOccupancyFilter.OCCUPIED) query = query.Where(x => activeApartmentIds.Contains(x.Id));
        if (occupancy == ApartmentOccupancyFilter.VACANT) query = query.Where(x => !activeApartmentIds.Contains(x.Id));

        pageIndex = Math.Max(pageIndex, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        pageIndex=Math.Min(pageIndex,Math.Max(1,(int)Math.Ceiling(total/(double)pageSize)));
        var occupiedCount = await query.CountAsync(x => activeApartmentIds.Contains(x.Id), ct);
        var inactiveCount = await query.CountAsync(x => x.Status == MasterDataStatus.INACTIVE, ct);
        var units = await query.OrderBy(x => x.FloorNumber).ThenBy(x => x.UnitNumber)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = units.Select(x => x.Id).ToArray();
        var currentOwnerships = await db.ApartmentOwnerships.AsNoTracking()
            .Where(x => ids.Contains(x.ApartmentUnitId) && x.StartDate <= today && x.EndDate == null).ToListAsync(ct);
        var ownerRecords = await residents.GetResidentsAsync(currentOwnerships.Select(x => x.OwnerResidentId).ToArray(), ct);
        var activeResidents = await residents.GetActiveAssociationsAsync(ids, today, ct);
        var items = units.Select(x =>
        {
            var owners = currentOwnerships.Where(o => o.ApartmentUnitId == x.Id).Select(o => ownerRecords.GetValueOrDefault(o.OwnerResidentId))
                .Where(o => o is not null).Cast<ResidentLookupItem>().OrderBy(o => o.ResidentCode).ToList();
            var currentResidents = (activeResidents.GetValueOrDefault(x.Id) ?? [])
                .Select(r => new ResidentLookupItem(r.ResidentId, r.ResidentCode, r.FullName)).ToArray();
            return new ApartmentListItem(x.Id, x.UnitNumber, x.FloorNumber, x.ApartmentUnitTypeId, x.ApartmentUnitType.Name, x.UsableAreaM2,
                x.Status.ToString(), owners, activeApartmentIds.Contains(x.Id), currentResidents);
        }).ToList();
        return Ok(new PagedApartmentsResponse(items, total, pageIndex, pageSize,
            new ApartmentSummary(total, occupiedCount, total - occupiedCount, inactiveCount)));
    }

    [HttpGet("floors"), Authorize(Policy = ApartmentsAuthorizationPolicies.Read)]
    public async Task<IReadOnlyList<int>> Floors(CancellationToken ct) =>
        await db.ApartmentUnits.AsNoTracking().Select(x=>x.FloorNumber).Distinct().OrderBy(x=>x).ToArrayAsync(ct);

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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241006)", ct);
        try
        {
            var creation=new PropFlow.Modules.Apartments.Infrastructure.ApartmentCreation(db,floorLimit,clock);
            var apartment=await creation.StageAsync(new(request.UnitNumber,request.FloorNumber,request.ApartmentUnitTypeId,request.UsableAreaM2,request.BedroomCount,request.BathroomCount,request.HandoverDate,request.Description,request.Status),Actor(),ct);
            await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
            var details=await Get(apartment.Id,ct);
            return CreatedAtAction(nameof(Get),new {id=apartment.Id},(details.Result as OkObjectResult)?.Value??details.Value);
        }
        catch(PropFlow.Modules.Apartments.Infrastructure.ApartmentCreationException error)
        {
            return StatusCode(error.Status,new ProblemDetails {Status=error.Status,Title=error.Message,Extensions={["code"]=error.Code}});
        }
    }

    [HttpPut("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> Update(Guid id, UpdateApartmentRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241006)", ct);
        var floorError = await ValidateFloorAsync(request.FloorNumber, ct);
        if (floorError is not null) return floorError;
        var validation = Validate(request.UnitNumber, request.FloorNumber, request.ApartmentUnitTypeId, request.UsableAreaM2, request.BedroomCount, request.BathroomCount);
        if (validation is not null) return validation;
        if (!await db.ApartmentUnitTypes.AnyAsync(x => x.Id == request.ApartmentUnitTypeId, ct)) return BadRequest(Problem("APARTMENT_UNIT_TYPE_NOT_FOUND", "Loại căn hộ không tồn tại."));
        var apartment = await db.ApartmentUnits.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (apartment is null) return NotFound();
        if (await db.ApartmentUnits.AnyAsync(x => x.Id != id && x.UnitNumber == request.UnitNumber.Trim(), ct)) return Conflict(Problem("APARTMENT_UNIT_NUMBER_DUPLICATE", "Mã căn hộ đã tồn tại."));
        apartment.Update(request.UnitNumber, request.FloorNumber, request.ApartmentUnitTypeId, request.UsableAreaM2,
            request.BedroomCount, request.BathroomCount, request.HandoverDate, request.Description, Actor(), clock.GetUtcNow());
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return NoContent();
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

    private async Task<ActionResult?> ValidateFloorAsync(int floor, CancellationToken ct)
    {
        var maximum = await floorLimit.GetMaximumFloorAsync(ct);
        if (maximum is null or <= 0)
            return BadRequest(Problem("BUILDING_NOT_CONFIGURED", "Vui lòng thiết lập số tầng chung cư trước khi lưu căn hộ."));
        return floor > maximum ? BadRequest(Problem("APARTMENT_FLOOR_OUT_OF_RANGE", $"Tầng căn hộ không được vượt quá {maximum} tầng của chung cư.")) : null;
    }

    private ActionResult? Validate(string unitNumber, int floorNumber, Guid apartmentUnitTypeId, decimal usableAreaM2, int? bedroomCount, int? bathroomCount)
    {
        var error=PropFlow.Modules.Apartments.Infrastructure.ApartmentCreation.Validate(unitNumber,floorNumber,apartmentUnitTypeId,usableAreaM2,bedroomCount,bathroomCount);
        return error is {} issue?BadRequest(Problem(issue.Code,issue.Message)):null;
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
public sealed record ApartmentListItem(Guid Id, string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, string UnitType, decimal UsableAreaM2, string Status, IReadOnlyList<ResidentLookupItem> Owners, bool IsOccupied, IReadOnlyList<ResidentLookupItem>? CurrentResidents = null);
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
