using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Infrastructure;

namespace PropFlow.Modules.PropertyAssets.Presentation.Controllers;

// Task 2 revised scope: published facility information for need-based discovery.
[ApiController,Route("api/v1/facility-guides"),Authorize(Policy=PropertyAssetsAuthorizationPolicies.Manage),Produces("application/json")]
public sealed class FacilityGuidesController(FacilityGuideService service) : ControllerBase
{
    [HttpGet,ProducesResponseType(typeof(FacilityGuidesManagement),200)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ManagementAsync(ct));
    [HttpPut("{facility:guid}"),ProducesResponseType(204),ProducesResponseType(409)]
    public async Task<IActionResult> Save(Guid facility,SaveFacilityGuide input,CancellationToken ct)
    {
        try { await service.SaveAsync(facility,input,Guid.Parse(User.FindFirstValue("sub")!),ct); return NoContent(); }
        catch(FacilityGuideFailure ex) { var problem=new ProblemDetails {Status=ex.Status,Title=ex.Message}; problem.Extensions["code"]=ex.Code;problem.Extensions["traceId"]=HttpContext.TraceIdentifier;return StatusCode(ex.Status,problem); }
    }
}
