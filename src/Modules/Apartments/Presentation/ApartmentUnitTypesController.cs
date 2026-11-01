using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.Modules.Apartments.Presentation;

[ApiController, Route("api/v1/apartment-unit-types")]
public sealed class ApartmentUnitTypesController(ApartmentsDbContext db) : ControllerBase
{
    [HttpGet, Authorize(Policy = ApartmentsAuthorizationPolicies.Read)]
    public async Task<IReadOnlyList<ApartmentUnitTypeResponse>> List(CancellationToken ct) =>
        await db.ApartmentUnitTypes.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new ApartmentUnitTypeResponse(x.Id, x.Name)).ToArrayAsync(ct);

    [HttpPost, Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public ActionResult<ApartmentUnitTypeResponse> Create(CreateApartmentUnitTypeRequest request) => Conflict(FixedCatalog());

    [HttpPut("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public ActionResult<ApartmentUnitTypeResponse> Update(Guid id, UpdateApartmentUnitTypeRequest request) => Conflict(FixedCatalog());

    [HttpDelete("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public IActionResult Delete(Guid id) => Conflict(FixedCatalog());

    private static ProblemDetails FixedCatalog() => new()
    {
        Status = 409,
        Title = "Danh mục loại căn hộ cố định",
        Detail = "Sử dụng 4 loại: Căn hộ thông thường, Studio, Officetel, Penthouse.",
        Extensions = { ["code"] = "APARTMENT_UNIT_TYPE_FIXED_CATALOG" }
    };
}
public sealed record ApartmentUnitTypeResponse(Guid Id, string Name);
public sealed record CreateApartmentUnitTypeRequest(string Name);
public sealed record UpdateApartmentUnitTypeRequest(string Name);
