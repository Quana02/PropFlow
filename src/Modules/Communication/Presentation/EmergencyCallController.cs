using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Communication.Application.EmergencyCalls;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;

namespace PropFlow.Modules.Communication.Presentation;

[ApiController, Route("api/v1/emergency-calls"), Authorize(Policy = SupportChatPolicies.Use)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmergencyCallController(IEmergencyCalls calls, IEmergencyRtcConfiguration rtc) : ControllerBase
{
    [HttpPost, Authorize(Policy = SupportChatPolicies.Resident), EnableRateLimiting("emergency-call-start"), RequestSizeLimit(2000)]
    public Task<ActionResult<EmergencyCallView>> Start(StartEmergencyCall request, CancellationToken ct) => Execute(user => calls.StartAsync(user, request, ct));
    [HttpGet("active")]
    public Task<ActionResult<EmergencyActiveCall>> Active(string connectionId, CancellationToken ct) => Execute(async user => new EmergencyActiveCall(await calls.ActiveAsync(user, connectionId, ct)));
    [HttpPost("{id:guid}/end"), EnableRateLimiting(SupportChatPolicies.RateLimit)]
    public Task<ActionResult<EmergencyCallView>> End(Guid id, CancellationToken ct) => Execute(user => calls.EndAsync(user, id, ct));
    [HttpPost("{id:guid}/retry"), EnableRateLimiting(SupportChatPolicies.RateLimit)]
    public Task<ActionResult<EmergencyCallView>> Retry(Guid id, CancellationToken ct) => Execute(user => calls.RetryAsync(user, id, ct));
    [HttpGet("rtc")]
    public Task<ActionResult<EmergencyRtcConfiguration>> Configuration(CancellationToken ct) => Execute(user => rtc.GetAsync(user, ct));
    private async Task<ActionResult<T>> Execute<T>(Func<Guid, Task<T>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var user)) return Unauthorized();
        try { return Ok(await action(user)); }
        catch (SupportChatFailure error) { return StatusCode(error.Status, new ProblemDetails
            { Status = error.Status, Title = error.Message, Extensions = { ["code"] = error.Code, ["traceId"] = HttpContext.TraceIdentifier } }); }
    }
}
