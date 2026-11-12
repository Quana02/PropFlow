using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Api.Composition;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Presentation;

namespace PropFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = ServiceRequestsAuthorizationPolicies.Manage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/manager/service-requests/{id:guid}/recommendation")]
[Produces("application/json")]
public sealed class ManagerServiceRequestRecommendationController(ManagerServiceRequestRecommendation recommendation) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ManagerServiceRequestRecommendationView>> Get(Guid id, CancellationToken ct) =>
        Map(await recommendation.GetAsync(id, ct));

    [HttpPost]
    [EnableRateLimiting(ManagerServiceRequestClassificationController.AnalyzeRateLimit)]
    public async Task<ActionResult<ManagerServiceRequestRecommendationView>> Analyze(Guid id, CancellationToken ct) =>
        Map(await recommendation.AnalyzeAsync(id, ct));

    [HttpPut("review")]
    public async Task<ActionResult<ManagerServiceRequestRecommendationView>> Review(Guid id,
        ReviewManagerServiceRequestRecommendation input, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var actor) || actor == Guid.Empty)
            return Map(new(null, 401, "invalid_session", "Phiên đăng nhập không hợp lệ."));
        return Map(await recommendation.ReviewAsync(id, actor, input, ct));
    }

    private ActionResult<ManagerServiceRequestRecommendationView> Map(RecommendationResult result)
    {
        if (result.View is { } view) return Ok(view);
        var problem = new ProblemDetails { Status = result.Status, Title = result.Message };
        problem.Extensions["code"] = result.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(result.Status, problem);
    }
}
