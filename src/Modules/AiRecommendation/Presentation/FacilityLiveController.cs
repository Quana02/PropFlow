using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;

namespace PropFlow.Modules.AiRecommendation.Presentation;

[ApiController, Route("api/v1/resident/facility-recommendations/live"), Authorize(Roles = "RESIDENT"), Produces("application/json")]
public sealed class FacilityLiveController(FacilityLiveSessions sessions, FacilityRecommendations recommendations) : ControllerBase
{
    [HttpPost("lookup"), ProducesResponseType(typeof(FacilityRecommendationView),200), ProducesResponseType(400), ProducesResponseType(401), ProducesResponseType(403)]
    public async Task<IActionResult> Lookup(FacilityRecommendationRequest input,CancellationToken ct)
    {
        try { return Ok(await recommendations.LookupAsync(Guid.Parse(User.FindFirstValue("sub")!),input,ct)); }
        catch(FacilityRecommendationFailure ex)
        {
            var problem=new ProblemDetails{Status=ex.Status,Title=ex.Message};
            problem.Extensions["code"]=ex.Code;problem.Extensions["traceId"]=HttpContext.TraceIdentifier;
            return StatusCode(ex.Status,problem);
        }
    }
    [HttpPost, ProducesResponseType(typeof(FacilityLiveSession), 200), ProducesResponseType(401), ProducesResponseType(403), ProducesResponseType(429), ProducesResponseType(503)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await sessions.CreateAsync(Guid.Parse(User.FindFirstValue("sub")!), ct)); }
        catch (FacilityRecommendationFailure ex)
        {
            var problem = new ProblemDetails { Status = ex.Status, Title = ex.Message };
            problem.Extensions["code"] = ex.Code; problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return StatusCode(ex.Status, problem);
        }
    }
}
