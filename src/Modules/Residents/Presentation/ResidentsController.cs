using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Text.RegularExpressions;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Residents.Application;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Presentation;

public static class ResidentsAuthorizationPolicies
{
    public const string Read = "residents.read";
    public const string Manage = "residents.manage";
}

[ApiController]
[Route("api/v1/residents")]
public sealed class ResidentsController(ResidentsDbContext db, IApartmentOverviewSource apartments,
    IApartmentResidentRelationshipSource apartmentRelationships, ResidentResidencyService residencyService,
    ResidentOnboardingService onboarding, ResidentProfileService profiles, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Read)]
    public async Task<ActionResult<PagedResidentsResponse>> List([FromQuery] string? search, [FromQuery] ResidentStatus? status,
        [FromQuery] Guid? apartmentUnitId, [FromQuery] ResidentApartmentRelationshipKind? relationshipKind,
        [FromQuery] ResidencyType? residencyType, [FromQuery] HouseholdRole? householdRole,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        pageIndex = Math.Max(1, pageIndex); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Residents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpper();
            query = query.Where(r => r.FullName.ToUpper().Contains(term) || r.ResidentCode.ToUpper().Contains(term)
                || (r.Email != null && r.Email.ToUpper().Contains(term)) || (r.PhoneNumber != null && r.PhoneNumber.Contains(term)));
        }
        if (status is not null) query = query.Where(r => r.Status == status);
        var filterDate = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var needsOwnerFilter = apartmentUnitId.HasValue || relationshipKind.HasValue;
        IReadOnlyList<Guid> currentOwnerIds = [];
        if (needsOwnerFilter)
        {
            currentOwnerIds = apartmentUnitId.HasValue
                ? await apartmentRelationships.GetCurrentOwnerResidentIdsAsync(apartmentUnitId.Value, filterDate, ct)
                : await apartmentRelationships.GetCurrentOwnerResidentIdsAsync(filterDate, ct);
        }
        if (apartmentUnitId.HasValue)
        {
            query = query.Where(r => currentOwnerIds.Contains(r.Id) || db.ResidentApartments.Any(a =>
                a.ResidentId == r.Id && a.ApartmentUnitId == apartmentUnitId.Value &&
                a.Status == ResidencyStatus.ACTIVE && a.StartDate <= filterDate &&
                (a.EndDate == null || a.EndDate >= filterDate)));
        }
        if (relationshipKind == ResidentApartmentRelationshipKind.OWNER_ONLY)
            query = query.Where(r => currentOwnerIds.Contains(r.Id) && !db.ResidentApartments.Any(a => a.ResidentId == r.Id && (!apartmentUnitId.HasValue || a.ApartmentUnitId == apartmentUnitId.Value) && a.Status == ResidencyStatus.ACTIVE && a.StartDate <= filterDate && (a.EndDate == null || a.EndDate >= filterDate)));
        else if (relationshipKind == ResidentApartmentRelationshipKind.RESIDENT_ONLY)
            query = query.Where(r => !currentOwnerIds.Contains(r.Id) && db.ResidentApartments.Any(a => a.ResidentId == r.Id && (!apartmentUnitId.HasValue || a.ApartmentUnitId == apartmentUnitId.Value) && a.Status == ResidencyStatus.ACTIVE && a.StartDate <= filterDate && (a.EndDate == null || a.EndDate >= filterDate)));
        else if (relationshipKind == ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT)
            query = query.Where(r => currentOwnerIds.Contains(r.Id) && db.ResidentApartments.Any(a => a.ResidentId == r.Id && (!apartmentUnitId.HasValue || a.ApartmentUnitId == apartmentUnitId.Value) && a.Status == ResidencyStatus.ACTIVE && a.StartDate <= filterDate && (a.EndDate == null || a.EndDate >= filterDate)));
        if (residencyType.HasValue)
            query = query.Where(r => db.ResidentApartments.Any(a => a.ResidentId == r.Id && (!apartmentUnitId.HasValue || a.ApartmentUnitId == apartmentUnitId.Value) && a.Status == ResidencyStatus.ACTIVE && a.StartDate <= filterDate && (a.EndDate == null || a.EndDate >= filterDate) && a.ResidencyType == residencyType));
        if (householdRole.HasValue)
            query = query.Where(r => db.ResidentApartments.Any(a => a.ResidentId == r.Id && (!apartmentUnitId.HasValue || a.ApartmentUnitId == apartmentUnitId.Value) && a.Status == ResidencyStatus.ACTIVE && a.StartDate <= filterDate && (a.EndDate == null || a.EndDate >= filterDate) && a.HouseholdRole == householdRole));
        var total = await query.CountAsync(ct);
        var residents = await query.OrderBy(r => r.FullName).Skip((pageIndex - 1) * pageSize).Take(pageSize)
            .Select(r => new ResidentListProjection(r.Id, r.ResidentCode, r.FullName, r.PhoneNumber, r.Email, r.Status, r.UserId != null,
                db.ResidentApartments.Where(a => a.ResidentId == r.Id && a.Status == ResidencyStatus.ACTIVE && a.EndDate == null)
                    .OrderByDescending(a => a.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD).Select(a => (Guid?)a.ApartmentUnitId).FirstOrDefault()))
            .ToListAsync(ct);
        var residentIds = residents.Select(r => r.Id).ToArray();
        var ownerships = await apartmentRelationships.GetOwnershipsAsync(residentIds, ct);
        var residencyApartmentIds = residents.Where(r => r.CurrentApartmentUnitId.HasValue).Select(r => r.CurrentApartmentUnitId!.Value).Distinct().ToArray();
        var apartmentReferences = await apartmentRelationships.GetApartmentsAsync(residencyApartmentIds, ct);
        var apartmentById = apartmentReferences.ToDictionary(x => x.Id);
        var today = filterDate;
        var exposeContact = User.IsInRole("MANAGER") || User.IsInRole("STAFF");
        var items = residents.Select(resident =>
        {
            var relations = ownerships.Where(x => x.ResidentId == resident.Id && x.StartDate <= today && x.EndDate is null)
                .Select(x => new ResidentApartmentRelationshipSummary(x.ApartmentUnitId, x.UnitNumber, x.FloorNumber, true, false))
                .ToDictionary(x => x.ApartmentUnitId);
            if (resident.CurrentApartmentUnitId is { } apartmentId && apartmentById.TryGetValue(apartmentId, out var apartment))
            {
                if (relations.TryGetValue(apartmentId, out var existing)) relations[apartmentId] = existing with { IsCurrentResident = true };
                else relations[apartmentId] = new(apartment.Id, apartment.UnitNumber, apartment.FloorNumber, false, true);
            }
            return new ResidentListItem(resident.Id, resident.ResidentCode, resident.FullName,
                exposeContact ? resident.PhoneNumber : null, exposeContact ? resident.Email : null,
                resident.Status.ToString(), resident.HasAccount, relations.Values.OrderBy(x => x.UnitNumber).ToArray());
        }).ToArray();
        return Ok(new PagedResidentsResponse(items, total, pageIndex, pageSize));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Read)]
    public async Task<ActionResult<ResidentDetailResponse>> Get(Guid id, CancellationToken ct)
    {
        var resident = await BuildDetailResponseAsync(id, DetailAccessForCurrentUser(), ct);
        return resident is null ? NotFound() : Ok(resident);
    }

    [HttpGet("me")]
    [Authorize(Roles = "RESIDENT")]
    public async Task<ActionResult<ResidentDetailResponse>> Me(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId)) return Forbid();
        var residentId = await db.Residents.AsNoTracking().Where(r => r.UserId == userId).Select(r => (Guid?)r.Id).SingleOrDefaultAsync(ct);
        var resident = residentId.HasValue ? await BuildDetailResponseAsync(residentId.Value, ResidentDetailAccess.Self, ct) : null;
        return resident is null ? NotFound() : Ok(resident);
    }

    [HttpPost]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<ResidentDetailResponse>> Create(CreateResidentRequest request, CancellationToken ct)
    {
        var validation = ValidateProfile(request.FullName, request.DateOfBirth, request.Nationality, request.IdentityType, request.IdentityNumber, request.IdentityIssuedDate, request.IdentityExpiryDate, request.PhoneNumber, request.Email, request.ApartmentUnitId, request.RelationshipKind, request.Residency);
        if (validation is not null) return validation;
        try
        {
            var residency = request.Residency;
            var onboardingResidency = residency is null
                ? new OnboardingResidency(request.ApartmentUnitId, HouseholdRole.HOUSEHOLD_HEAD, ResidencyType.OWNER_OCCUPIED, null, null, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), null, null)
                : new OnboardingResidency(request.ApartmentUnitId, residency.HouseholdRole, residency.ResidencyType, residency.HouseholdHeadResidencyId, residency.RelationshipToHead, residency.StartDate, residency.EndDate, residency.Note);
            var result = await onboarding.OnboardAsync(new(request.FullName, request.DateOfBirth, request.Gender, request.Nationality, request.IdentityType, request.IdentityNumber, request.IdentityIssuedDate, request.IdentityExpiryDate, request.PhoneNumber, request.Email, request.Note, (OnboardingRelationshipKind)request.RelationshipKind, onboardingResidency), ActorId(), ct);
            var resident = result.Resident;
            var response = await BuildDetailResponseAsync(resident.Id, ResidentDetailAccess.Manager, ct);
            return CreatedAtAction(nameof(Get), new { id = resident.Id }, response!);
        }
        catch (ResidentResidencyException ex)
        {
            return ResidencyProblem(ex);
        }
        catch (ResidentDuplicateException ex)
        {
            return DuplicateProblem(ex);
        }
        catch (ApartmentOwnershipException ex)
        {
            var details = new ProblemDetails { Status = ex.StatusCode, Title = ex.Message };
            details.Extensions["code"] = ex.Code;
            return StatusCode(ex.StatusCode, details);
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> Update(Guid id, UpdateResidentRequest request, CancellationToken ct)
    {
        var validation = ValidateProfile(request.FullName, request.DateOfBirth, request.Nationality, request.IdentityType, request.IdentityNumber, request.IdentityIssuedDate, request.IdentityExpiryDate, request.PhoneNumber, request.Email, Guid.Empty, null, null);
        if (validation is not null) return validation;
        try
        {
            var updated = await profiles.UpdateAsync(id, new(request.FullName, request.DateOfBirth, request.Gender,
                request.Nationality, request.IdentityType, request.IdentityNumber, request.IdentityIssuedDate,
                request.IdentityExpiryDate, request.PhoneNumber, request.Email, request.Note), ActorId(), ct);
            return updated ? NoContent() : NotFound();
        }
        catch (ResidentDuplicateException ex)
        {
            return DuplicateProblem(ex);
        }
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> SetStatus(Guid id, SetResidentStatusRequest request, CancellationToken ct)
    {
        var resident = await db.Residents.SingleOrDefaultAsync(r => r.Id == id, ct); if (resident is null) return NotFound();
        switch (request.Status)
        {
            case ResidentStatus.ACTIVE: resident.Activate(ActorId(), clock.GetUtcNow()); break;
            case ResidentStatus.INACTIVE: resident.Deactivate(ActorId(), clock.GetUtcNow()); break;
            case ResidentStatus.MOVED_OUT: resident.MarkMovedOut(ActorId(), clock.GetUtcNow()); break;
            default:
                var problem = new ProblemDetails { Status = 400, Title = "Trạng thái cư dân không hợp lệ." };
                problem.Extensions["code"] = "resident_status_invalid";
                return BadRequest(problem);
        }
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("{id:guid}/residencies")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<ResidencyItem>> AddResidency(Guid id, CreateResidencyRequest request, CancellationToken ct)
    {
        try
        {
            var relation = await residencyService.CreateAsync(id, new(request.ApartmentUnitId, request.HouseholdRole, request.ResidencyType, request.HouseholdHeadResidencyId, request.RelationshipToHead, request.StartDate, request.EndDate, request.Note), ActorId(), ct);
            var apartment = (await apartmentRelationships.GetApartmentsAsync([relation.ApartmentUnitId], ct)).Single();
            return Ok(new ResidencyItem(relation.Id, relation.ApartmentUnitId, apartment.UnitNumber, apartment.FloorNumber,
                relation.HouseholdRole.ToString(), relation.ResidencyType.ToString(), relation.HouseholdHeadResidencyId, null, null,
                relation.RelationshipToHead?.ToString(), relation.StartDate, relation.EndDate, relation.Status.ToString(), relation.Note));
        }
        catch (ResidentResidencyException ex) { return ResidencyProblem(ex); }
    }

    [HttpPatch("{id:guid}/residencies/{residencyId:guid}/end")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> EndResidency(Guid id, Guid residencyId, EndResidencyRequest request, CancellationToken ct)
    {
        try { await residencyService.EndAsync(id, residencyId, request.EndDate, ActorId(), ct); return NoContent(); }
        catch (ResidentResidencyException ex) { return ResidencyProblem(ex); }
    }

    [HttpPost("{id:guid}/residencies/move")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<ResidencyItem>> MoveResidency(Guid id, MoveResidencyRequest request, CancellationToken ct)
    {
        try
        {
            var target = request.Target;
            var relation = await residencyService.MoveAsync(id, request.SourceResidencyId, new(target.ApartmentUnitId, target.HouseholdRole, target.ResidencyType, target.HouseholdHeadResidencyId, target.RelationshipToHead, target.StartDate, target.EndDate, target.Note), ActorId(), ct);
            var apartment = (await apartmentRelationships.GetApartmentsAsync([relation.ApartmentUnitId], ct)).Single();
            return Ok(new ResidencyItem(relation.Id, relation.ApartmentUnitId, apartment.UnitNumber, apartment.FloorNumber, relation.HouseholdRole.ToString(), relation.ResidencyType.ToString(), relation.HouseholdHeadResidencyId, null, null, relation.RelationshipToHead?.ToString(), relation.StartDate, relation.EndDate, relation.Status.ToString(), relation.Note));
        }
        catch (ResidentResidencyException ex) { return ResidencyProblem(ex); }
    }

    [HttpGet("{id:guid}/residency-candidates")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Manage)]
    public async Task<IReadOnlyList<ActiveApartmentOption>> ResidencyCandidates(Guid id, CancellationToken ct)
    {
        var activeIds = await db.ResidentApartments.AsNoTracking().Where(x => x.ResidentId == id && x.Status == ResidencyStatus.ACTIVE && x.EndDate == null).Select(x => x.ApartmentUnitId).ToArrayAsync(ct);
        return (await apartments.GetActiveApartmentsAsync(ct)).Where(x => !activeIds.Contains(x.Id)).ToArray();
    }

    [HttpGet("apartments")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Read)]
    public Task<IReadOnlyList<ActiveApartmentOption>> ActiveApartments(CancellationToken ct) => apartments.GetActiveApartmentsAsync(ct);

    [HttpGet("apartments/{apartmentUnitId:guid}/eligible-household-heads")]
    [Authorize(Policy = ResidentsAuthorizationPolicies.Manage)]
    public Task<IReadOnlyList<EligibleHouseholdHead>> EligibleHouseholdHeads(Guid apartmentUnitId, CancellationToken ct) => residencyService.EligibleHeadsAsync(apartmentUnitId, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), ct);

    private Guid? ActorId() => Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

    private async Task<ResidentDetailResponse?> BuildDetailResponseAsync(Guid residentId, ResidentDetailAccess access, CancellationToken ct)
    {
        var profile = await db.Residents.AsNoTracking().Where(r => r.Id == residentId)
            .Select(r => new ResidentProfileProjection(r.Id, r.ResidentCode, r.FullName, r.DateOfBirth, r.Gender,
                r.Nationality, r.IdentityType, r.IdentityNumber, r.IdentityIssuedDate, r.IdentityExpiryDate,
                r.PhoneNumber, r.Email, r.Note, r.Status, r.UserId != null))
            .SingleOrDefaultAsync(ct);
        if (profile is null) return null;

        var residencyRows = await db.ResidentApartments.AsNoTracking().Where(a => a.ResidentId == residentId)
            .OrderByDescending(a => a.StartDate)
            .Select(a => new ResidencyProjection(a.Id, a.ApartmentUnitId, a.HouseholdRole, a.ResidencyType,
                a.HouseholdHeadResidencyId, a.RelationshipToHead, a.StartDate, a.EndDate, a.Status, a.Note))
            .ToArrayAsync(ct);
        var apartmentIds = residencyRows.Select(x => x.ApartmentUnitId).Distinct().ToArray();
        var apartmentById = (await apartmentRelationships.GetApartmentsAsync(apartmentIds, ct)).ToDictionary(x => x.Id);
        var headIds = residencyRows.Where(x => x.HouseholdHeadResidencyId.HasValue).Select(x => x.HouseholdHeadResidencyId!.Value).Distinct().ToArray();
        var heads = headIds.Length == 0
            ? []
            : await db.ResidentApartments.AsNoTracking().Where(x => headIds.Contains(x.Id))
                .Select(x => new HouseholdHeadProjection(x.Id, x.Resident!.Id, x.Resident.FullName, x.Resident.ResidentCode))
                .ToArrayAsync(ct);
        var headByResidencyId = heads.ToDictionary(x => x.ResidencyId);
        var residencies = residencyRows.Select(row =>
        {
            apartmentById.TryGetValue(row.ApartmentUnitId, out var apartment);
            HouseholdHeadProjection? head = null;
            if (row.HouseholdHeadResidencyId.HasValue) headByResidencyId.TryGetValue(row.HouseholdHeadResidencyId.Value, out head);
            return new ResidencyItem(row.Id, row.ApartmentUnitId, apartment?.UnitNumber ?? "—", apartment?.FloorNumber ?? 0,
                row.HouseholdRole.ToString(), row.ResidencyType.ToString(), row.HouseholdHeadResidencyId, head?.FullName, head?.ResidentCode,
                row.RelationshipToHead?.ToString(), row.StartDate, row.EndDate, row.Status.ToString(), row.Note);
        }).ToArray();
        var ownerships = (await apartmentRelationships.GetOwnershipsAsync([residentId], ct))
            .Select(x => new OwnershipItem(x.Id, x.ApartmentUnitId, x.UnitNumber, x.FloorNumber, x.StartDate, x.EndDate))
            .ToArray();
        var full = access is ResidentDetailAccess.Manager or ResidentDetailAccess.Self;
        var operational = access == ResidentDetailAccess.Staff;
        return new(profile.Id, profile.ResidentCode, profile.FullName, full ? profile.DateOfBirth : null, full ? profile.Gender : null,
            full ? profile.Nationality : null, full ? profile.IdentityType : null, full ? profile.IdentityNumber : null,
            full ? profile.IdentityIssuedDate : null, full ? profile.IdentityExpiryDate : null,
            full || operational ? profile.PhoneNumber : null, full || operational ? profile.Email : null,
            full ? profile.Note : null, profile.Status.ToString(),
            profile.HasAccount, ownerships, residencies);
    }

    private ResidentDetailAccess DetailAccessForCurrentUser() =>
        User.IsInRole("MANAGER") ? ResidentDetailAccess.Manager :
        User.IsInRole("STAFF") ? ResidentDetailAccess.Staff : ResidentDetailAccess.Accountant;
    private ObjectResult ResidencyProblem(ResidentResidencyException ex)
    {
        var details = new ProblemDetails { Status = ex.StatusCode, Title = ex.Message };
        details.Extensions["code"] = ex.Code;
        return StatusCode(ex.StatusCode, details);
    }

    private ObjectResult DuplicateProblem(ResidentDuplicateException ex)
    {
        var details = new ValidationProblemDetails(new Dictionary<string, string[]> { [ex.Field] = [ex.Message] })
        {
            Status = ex.StatusCode,
            Title = ex.Message
        };
        details.Extensions["code"] = ex.Code;
        return StatusCode(ex.StatusCode, details);
    }

    private ActionResult? ValidateProfile(string fullName, DateOnly? dateOfBirth, string? nationality, string? identityType, string? identityNumber, DateOnly? identityIssuedDate, DateOnly? identityExpiryDate, string? phoneNumber, string? email, Guid apartmentUnitId, ResidentApartmentRelationshipKind? relationshipKind, CreateResidencyRequest? residency)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(fullName)) errors["fullName"] = ["Họ và tên là bắt buộc."];
        if (!dateOfBirth.HasValue) errors["dateOfBirth"] = ["Ngày sinh là bắt buộc."];
        else if (dateOfBirth > DateOnly.FromDateTime(clock.GetLocalNow().DateTime)) errors["dateOfBirth"] = ["Ngày sinh không được ở tương lai."];
        if (string.IsNullOrWhiteSpace(nationality)) errors["nationality"] = ["Quốc tịch là bắt buộc."];
        if (!string.IsNullOrWhiteSpace(fullName) && fullName.Trim().Length > 150) errors["fullName"] = ["Họ và tên không được vượt quá 150 ký tự."];
        if (!string.IsNullOrWhiteSpace(nationality) && nationality.Trim().Length > 80) errors["nationality"] = ["Quốc tịch không được vượt quá 80 ký tự."];
        var normalizedIdentityType = ResidentProfileNormalization.IdentityType(identityType);
        var normalizedIdentityNumber = ResidentProfileNormalization.IdentityNumber(identityNumber);
        if (string.IsNullOrWhiteSpace(normalizedIdentityType)) errors["identityType"] = ["Vui lòng chọn loại giấy tờ."];
        else if (normalizedIdentityType is not ("CCCD" or "CMND")) errors["identityType"] = ["Loại giấy tờ chỉ được phép là CCCD hoặc CMND."];
        if (string.IsNullOrWhiteSpace(normalizedIdentityNumber)) errors["identityNumber"] = ["Vui lòng nhập số giấy tờ."];
        else if (!Regex.IsMatch(identityNumber!.Trim(), "^[0-9\\s./-]+$")) errors["identityNumber"] = ["Số giấy tờ chỉ được gồm chữ số và dấu phân cách."];
        else if (normalizedIdentityType == "CCCD" && !Regex.IsMatch(normalizedIdentityNumber, "^\\d{12}$")) errors["identityNumber"] = ["Số CCCD phải gồm đúng 12 chữ số."];
        else if (normalizedIdentityType == "CMND" && !Regex.IsMatch(normalizedIdentityNumber, "^(\\d{9}|\\d{12})$")) errors["identityNumber"] = ["Số CMND phải gồm 9 hoặc 12 chữ số."];
        var normalizedPhone = phoneNumber?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPhone)) errors["phoneNumber"] = ["Số điện thoại là bắt buộc."];
        else if (!Regex.IsMatch(normalizedPhone, "^(?:\\+84|0)\\d{9,10}$")) errors["phoneNumber"] = ["Số điện thoại không đúng định dạng."];
        if (!string.IsNullOrWhiteSpace(email) && (email.Trim().Length > 255 || !MailAddress.TryCreate(email.Trim(), out _))) errors["email"] = ["Email không đúng định dạng."];
        if (identityIssuedDate > DateOnly.FromDateTime(clock.GetLocalNow().DateTime)) errors["identityIssuedDate"] = ["Ngày cấp không được ở tương lai."];
        if (identityExpiryDate.HasValue && identityIssuedDate.HasValue && identityExpiryDate < identityIssuedDate) errors["identityExpiryDate"] = ["Ngày hết hạn phải từ ngày cấp trở đi."];
        if (relationshipKind.HasValue)
        {
            if (apartmentUnitId == Guid.Empty) errors["apartmentUnitId"] = ["Bắt buộc chọn căn hộ."];
            if (relationshipKind == ResidentApartmentRelationshipKind.OWNER_ONLY && residency is not null) errors["residency"] = ["Chủ sở hữu không có thông tin cư trú trong lần tạo này."];
            if ((relationshipKind is ResidentApartmentRelationshipKind.RESIDENT_ONLY or ResidentApartmentRelationshipKind.OWNER_AND_RESIDENT) && residency is null) errors["residency"] = ["Thông tin cư trú là bắt buộc."];
            if (residency is not null)
            {
                if (residency.ApartmentUnitId != apartmentUnitId) errors["residency.apartmentUnitId"] = ["Căn hộ trong thông tin cư trú không khớp."];
                if (relationshipKind == ResidentApartmentRelationshipKind.RESIDENT_ONLY && residency.ResidencyType == ResidencyType.OWNER_OCCUPIED)
                    errors["residency.residencyType"] = ["Quan hệ chỉ cư trú không được dùng hình thức chủ sở hữu đang cư trú."];
                if (residency.HouseholdRole == HouseholdRole.HOUSEHOLD_HEAD)
                {
                    if (residency.HouseholdHeadResidencyId.HasValue) errors["residency.householdHeadResidencyId"] = ["Chủ hộ không được tham chiếu một chủ hộ khác."];
                    if (residency.RelationshipToHead.HasValue) errors["residency.relationshipToHead"] = ["Chủ hộ không có quan hệ với một chủ hộ khác."];
                }
                else if (residency.HouseholdRole == HouseholdRole.HOUSEHOLD_MEMBER)
                {
                    if (!residency.HouseholdHeadResidencyId.HasValue) errors["residency.householdHeadResidencyId"] = ["Vui lòng chọn chủ hộ."];
                    if (!residency.RelationshipToHead.HasValue) errors["residency.relationshipToHead"] = ["Vui lòng chọn quan hệ với chủ hộ."];
                }
            }
        }
        return errors.Count == 0 ? null : BadRequest(new ValidationProblemDetails(errors) { Title = "Vui lòng kiểm tra thông tin đã nhập.", Status = 400 });
    }
}

internal enum ResidentDetailAccess { Manager, Staff, Accountant, Self }

public sealed record PagedResidentsResponse(IReadOnlyList<ResidentListItem> Items, int TotalCount, int PageIndex, int PageSize);
public sealed record ResidentListItem(Guid Id, string ResidentCode, string FullName, string? PhoneNumber, string? Email, string Status, bool HasAccount, IReadOnlyList<ResidentApartmentRelationshipSummary> ApartmentRelationships);
public sealed record ResidentApartmentRelationshipSummary(Guid ApartmentUnitId, string UnitNumber, int FloorNumber, bool IsCurrentOwner, bool IsCurrentResident);
public sealed record ResidentDetailResponse(Guid Id, string ResidentCode, string FullName, DateOnly? DateOfBirth, string? Gender, string? Nationality, string? IdentityType, string? IdentityNumber, DateOnly? IdentityIssuedDate, DateOnly? IdentityExpiryDate, string? PhoneNumber, string? Email, string? Note, string Status, bool HasAccount, IReadOnlyList<OwnershipItem> Ownerships, IReadOnlyList<ResidencyItem> Residencies);
public sealed record OwnershipItem(Guid Id, Guid ApartmentUnitId, string UnitNumber, int FloorNumber, DateOnly StartDate, DateOnly? EndDate);
public sealed record ResidencyItem(Guid Id, Guid ApartmentUnitId, string UnitNumber, int FloorNumber, string HouseholdRole, string ResidencyType, Guid? HouseholdHeadResidencyId, string? HouseholdHeadName, string? HouseholdHeadResidentCode, string? RelationshipToHead, DateOnly StartDate, DateOnly? EndDate, string Status, string? Note);
file sealed record ResidentListProjection(Guid Id, string ResidentCode, string FullName, string? PhoneNumber, string? Email, ResidentStatus Status, bool HasAccount, Guid? CurrentApartmentUnitId);
file sealed record ResidentProfileProjection(Guid Id, string ResidentCode, string FullName, DateOnly? DateOfBirth, string? Gender, string? Nationality, string? IdentityType, string? IdentityNumber, DateOnly? IdentityIssuedDate, DateOnly? IdentityExpiryDate, string? PhoneNumber, string? Email, string? Note, ResidentStatus Status, bool HasAccount);
file sealed record ResidencyProjection(Guid Id, Guid ApartmentUnitId, HouseholdRole HouseholdRole, ResidencyType ResidencyType, Guid? HouseholdHeadResidencyId, HouseholdRelationship? RelationshipToHead, DateOnly StartDate, DateOnly? EndDate, ResidencyStatus Status, string? Note);
file sealed record HouseholdHeadProjection(Guid ResidencyId, Guid ResidentId, string FullName, string ResidentCode);
public enum ResidentApartmentRelationshipKind { OWNER_ONLY, RESIDENT_ONLY, OWNER_AND_RESIDENT }
public sealed record CreateResidentRequest(string FullName, DateOnly? DateOfBirth, string? Gender, string? Nationality, string? IdentityType, string? IdentityNumber, DateOnly? IdentityIssuedDate, DateOnly? IdentityExpiryDate, string? PhoneNumber, string? Email, string? Note, Guid ApartmentUnitId, ResidentApartmentRelationshipKind RelationshipKind, CreateResidencyRequest? Residency);
public sealed record UpdateResidentRequest(string FullName, DateOnly? DateOfBirth, string? Gender, string? Nationality, string? IdentityType, string? IdentityNumber, DateOnly? IdentityIssuedDate, DateOnly? IdentityExpiryDate, string? PhoneNumber, string? Email, string? Note);
public sealed record SetResidentStatusRequest(ResidentStatus Status);
public sealed record CreateResidencyRequest(Guid ApartmentUnitId, HouseholdRole HouseholdRole, ResidencyType ResidencyType, Guid? HouseholdHeadResidencyId, HouseholdRelationship? RelationshipToHead, DateOnly StartDate, DateOnly? EndDate, string? Note);
public sealed record EndResidencyRequest(DateOnly EndDate);
public sealed record MoveResidencyRequest(Guid SourceResidencyId, CreateResidencyRequest Target);
