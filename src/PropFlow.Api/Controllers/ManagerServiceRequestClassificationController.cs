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
[Route("api/v1/manager/service-requests/{id:guid}/classification")]
[Produces("application/json")]
public sealed class ManagerServiceRequestClassificationController(ManagerServiceRequestClassification classification) : ControllerBase
{
    public const string AnalyzeRateLimit = "service-request-ai-classification";

    [HttpGet]
    public async Task<ActionResult<ManagerServiceRequestClassificationView>> Get(Guid id, CancellationToken ct) =>
        Map(await classification.GetAsync(id, ct));

    [HttpPost]
    [EnableRateLimiting(AnalyzeRateLimit)]
    public async Task<ActionResult<ManagerServiceRequestClassificationView>> Analyze(Guid id, CancellationToken ct) =>
        Map(await classification.AnalyzeAsync(id, ct));

    [HttpPut("review")]
    public async Task<ActionResult<ManagerServiceRequestClassificationView>> Review(Guid id,
        ReviewManagerServiceRequestClassification input, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var actor) || actor == Guid.Empty)
            return Map(new(null, 401, "invalid_session", "Phiên đăng nhập không hợp lệ."));
        return Map(await classification.ReviewAsync(id, actor, input, ct));
    }

    private ActionResult<ManagerServiceRequestClassificationView> Map(ClassificationResult result)
    {
        if (result.View is { } view) return Ok(view);
        var problem = new ProblemDetails { Status = result.Status, Title = result.Message };
        problem.Extensions["code"] = result.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(result.Status, problem);
    }
}
