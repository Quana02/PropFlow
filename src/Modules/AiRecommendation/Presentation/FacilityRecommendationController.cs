using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;

namespace PropFlow.Modules.AiRecommendation.Presentation;

[ApiController, Route("api/v1/resident/facility-recommendations"), Authorize(Roles = "RESIDENT"), Produces("application/json")]
public sealed class FacilityRecommendationController(FacilityRecommendations service, FacilitySpeech speech) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet, ProducesResponseType(typeof(ResidentFacilityCatalogue), 200)]
    public Task<IActionResult> Catalogue(CancellationToken ct) => Run(async () => Ok(await service.CatalogueAsync(Actor, ct)));
    [HttpPost, ProducesResponseType(typeof(FacilityRecommendationView), 200), ProducesResponseType(429), ProducesResponseType(409)]
    public Task<IActionResult> Recommend(FacilityRecommendationRequest input, CancellationToken ct) => Run(async () => {
        var view = await service.RecommendAsync(Actor, input, ct);
        return Ok(view with { SpeechId = speech.Prepare(Actor, view) });
    });
    [HttpPost("speech"), ProducesResponseType(typeof(FacilitySpeechAudio), 200), ProducesResponseType(429)]
    public Task<IActionResult> Speech(FacilitySpeechRequest input, CancellationToken ct) => Run(async () => {
        await service.CatalogueAsync(Actor, ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(await speech.GetAsync(Actor, input, ct));
    });
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (FacilityRecommendationFailure ex)
        {
            var problem = new ProblemDetails { Status = ex.Status, Title = ex.Message };
            problem.Extensions["code"] = ex.Code; problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return StatusCode(ex.Status, problem);
        }
    }
}
