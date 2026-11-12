using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Api.Composition;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Presentation;

namespace PropFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = ServiceRequestsAuthorizationPolicies.SubmitAsResident)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/resident/service-requests/draft-check")]
[Produces("application/json")]
public sealed class ResidentServiceRequestDraftController(ResidentServiceRequestDraftChecker drafts) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("resident-service-request-draft-check")]
    [ProducesResponseType(typeof(ResidentServiceRequestDraftReview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ResidentServiceRequestDraftReview>> Check(CheckResidentServiceRequestDraft input, CancellationToken ct)
    {
        Guid.TryParse(User.FindFirstValue("sub"), out var actor);
        var result = await drafts.CheckAsync(actor, input.Description, ct);
        if (result.Review is { } review) return Ok(review);
        var problem = new ProblemDetails { Status = result.Status, Title = result.Message };
        problem.Extensions["code"] = result.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(result.Status, problem);
    }
}
