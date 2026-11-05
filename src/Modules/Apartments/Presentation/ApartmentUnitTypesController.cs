using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Apartments.Application;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;

namespace PropFlow.Modules.Apartments.Presentation;

[ApiController, Route("api/v1/apartment-unit-types")]
public sealed class ApartmentUnitTypesController(ApartmentUnitTypeService types) : ControllerBase
{
    [HttpGet, Authorize(Policy = ApartmentsAuthorizationPolicies.Read)]
    public async Task<IReadOnlyList<ApartmentUnitTypeResponse>> List(CancellationToken ct) =>
        (await types.ListAsync(ct)).Select(Map).ToArray();

    [HttpPost, Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<ApartmentUnitTypeResponse>> Create(CreateApartmentUnitTypeRequest request, CancellationToken ct)
    {
        try
        {
            var type = await types.SaveAsync(null, request.Name, request.DefaultUsableAreaM2, request.DefaultBedroomCount, request.DefaultBathroomCount, ct);
            return Created($"/api/v1/apartment-unit-types/{type.Id}", Map(type));
        }
        catch (ApartmentUnitTypeException exception) { return Failure(exception); }
    }

    [HttpPut("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<ApartmentUnitTypeResponse>> Update(Guid id, UpdateApartmentUnitTypeRequest request, CancellationToken ct)
    {
        try { return Ok(Map(await types.SaveAsync(id, request.Name, request.DefaultUsableAreaM2, request.DefaultBedroomCount, request.DefaultBathroomCount, ct))); }
        catch (ApartmentUnitTypeException exception) { return Failure(exception); }
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = ApartmentsAuthorizationPolicies.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await types.DeleteAsync(id, ct); return NoContent(); }
        catch (ApartmentUnitTypeException exception) { return Failure(exception); }
    }

    private ObjectResult Failure(ApartmentUnitTypeException exception) => StatusCode(exception.StatusCode,
        new ProblemDetails { Status = exception.StatusCode, Title = exception.Message, Extensions = { ["code"] = exception.Code } });
    private static ApartmentUnitTypeResponse Map(ApartmentUnitType type) => new(type.Id, type.Name, type.DefaultUsableAreaM2, type.DefaultBedroomCount, type.DefaultBathroomCount);
}
public sealed record ApartmentUnitTypeResponse(Guid Id, string Name, decimal? DefaultUsableAreaM2 = null, int? DefaultBedroomCount = null, int? DefaultBathroomCount = null);
public sealed record CreateApartmentUnitTypeRequest(string Name, decimal? DefaultUsableAreaM2 = null, int? DefaultBedroomCount = null, int? DefaultBathroomCount = null);
public sealed record UpdateApartmentUnitTypeRequest(string Name, decimal? DefaultUsableAreaM2 = null, int? DefaultBedroomCount = null, int? DefaultBathroomCount = null);
