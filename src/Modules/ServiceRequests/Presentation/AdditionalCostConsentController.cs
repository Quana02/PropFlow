using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Infrastructure;

namespace PropFlow.Modules.ServiceRequests.Presentation;

[ApiController, Route("api/v1")]
public sealed class AdditionalCostConsentController(AdditionalCostConsentService consent) : ControllerBase
{
    [HttpPost("staff/service-requests/{id:guid}/additional-cost")]
    [Authorize(Policy = ServiceRequestsAuthorizationPolicies.WorkAssigned)]
    public Task<ActionResult<AdditionalCostConsentView>> Propose(Guid id, ProposeAdditionalCostRequest request, CancellationToken ct) =>
        Execute(actor => consent.ProposeAsync(actor, id, request, ct));

    [HttpPost("resident/service-requests/{id:guid}/additional-cost/response")]
    [Authorize(Policy = ServiceRequestsAuthorizationPolicies.SubmitAsResident)]
    public Task<ActionResult<AdditionalCostConsentView>> Respond(Guid id, RespondAdditionalCostRequest request, CancellationToken ct) =>
        Execute(actor => consent.RespondAsync(actor, id, request, ct));

    private async Task<ActionResult<AdditionalCostConsentView>> Execute(Func<Guid, Task<AdditionalCostConsentView>> operation)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var actor)) return Unauthorized();
        try { return Ok(await operation(actor)); }
        catch (ServiceRequestCostFailure failure)
        {
            return StatusCode(failure.Status, new ProblemDetails { Status = failure.Status, Title = failure.Message,
                Extensions = { ["code"] = failure.Code, ["traceId"] = HttpContext.TraceIdentifier } });
        }
    }
}
