using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.Modules.Apartments.Presentation;

[ApiController, Route("api/v1/apartment-unit-types")]
public sealed class ApartmentUnitTypesController(ApartmentsDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet, Authorize(Policy = ApartmentsAuthorizationPolicies.Read)]
    public async Task<IReadOnlyList<ApartmentUnitTypeResponse>> List(CancellationToken ct) =>
        await db.ApartmentUnitTypes.AsNoTracking().OrderBy(x => x.Name).Select(x => new ApartmentUnitTypeResponse(x.Id, x.Name)).ToArrayAsync(ct);

    [HttpPost, Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<ApartmentUnitTypeResponse>> Create(CreateApartmentUnitTypeRequest request, CancellationToken ct)
    {
        var invalid = ValidateName(request.Name, out var name);
        if (invalid is not null) return BadRequest(invalid);
        if (await HasDuplicateName(name, null, ct)) return Conflict(DuplicateProblem());
        var type = new ApartmentUnitType(name, clock.GetUtcNow());
        db.ApartmentUnitTypes.Add(type);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(DuplicateProblem()); }
        return Created($"api/v1/apartment-unit-types/{type.Id}", new ApartmentUnitTypeResponse(type.Id, type.Name));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<ApartmentUnitTypeResponse>> Update(Guid id, UpdateApartmentUnitTypeRequest request, CancellationToken ct)
    {
        var invalid = ValidateName(request.Name, out var name);
        if (invalid is not null) return BadRequest(invalid);
        var type = await db.ApartmentUnitTypes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (type is null) return NotFound(Problem("APARTMENT_UNIT_TYPE_NOT_FOUND", "Không tìm thấy loại căn hộ.", 404));
        if (await HasDuplicateName(name, id, ct)) return Conflict(DuplicateProblem());
        type.Rename(name, clock.GetUtcNow());
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(DuplicateProblem()); }
        return Ok(new ApartmentUnitTypeResponse(type.Id, type.Name));
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var type = await db.ApartmentUnitTypes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (type is null) return NotFound(Problem("APARTMENT_UNIT_TYPE_NOT_FOUND", "Không tìm thấy loại căn hộ.", 404));
        if (await db.ApartmentUnits.AnyAsync(x => x.ApartmentUnitTypeId == id, ct))
            return Conflict(InUseProblem());
        db.ApartmentUnitTypes.Remove(type);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // The FK is the final guard if an apartment starts referencing the type
            // between the usage check and DELETE.
            return Conflict(InUseProblem());
        }
        return NoContent();
    }

    private Task<bool> HasDuplicateName(string name, Guid? excludedId, CancellationToken ct)
    {
        var normalized = name.ToUpper();
        return db.ApartmentUnitTypes.AnyAsync(x => x.Id != excludedId && x.Name.ToUpper() == normalized, ct);
    }

    private static ProblemDetails? ValidateName(string? value, out string name)
    {
        name = value?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(name) || name.Length > 80
            ? Problem("APARTMENT_UNIT_TYPE_NAME_INVALID", "Tên loại căn hộ là bắt buộc và tối đa 80 ký tự.", 400)
            : null;
    }

    private static ProblemDetails DuplicateProblem() =>
        Problem("APARTMENT_UNIT_TYPE_DUPLICATE", "Loại căn hộ đã tồn tại.", 409);

    private static ProblemDetails InUseProblem() =>
        Problem("APARTMENT_TYPE_IN_USE", "Không thể xóa loại căn hộ này vì đang được sử dụng.", 409);

    private static ProblemDetails Problem(string code, string detail, int status) => new()
    {
        Title = "Không thể hoàn tất yêu cầu loại căn hộ",
        Detail = detail,
        Status = status,
        Extensions = { ["code"] = code }
    };
}

public sealed record ApartmentUnitTypeResponse(Guid Id, string Name);
public sealed record CreateApartmentUnitTypeRequest(string Name);
public sealed record UpdateApartmentUnitTypeRequest(string Name);
