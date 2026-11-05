using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;

namespace PropFlow.Modules.Communication.Presentation;

[ApiController, Route("api/v1/support-chat"), Authorize(Policy = SupportChatPolicies.Use)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportChatController(ISupportChat chat) : ControllerBase
{
    [HttpGet("conversations")]
    public Task<ActionResult<SupportConversationPage>> List(int page = 1, CancellationToken ct = default) => Execute(a => chat.ListAsync(a, page, ct));
    [HttpGet("conversations/{id:guid}")]
    public Task<ActionResult<SupportConversationView>> Get(Guid id, CancellationToken ct) => Execute(a => chat.GetAsync(a, id, ct));
    [HttpGet("conversations/{id:guid}/messages")]
    public Task<ActionResult<SupportMessagePage>> Messages(Guid id, long? before, CancellationToken ct) => Execute(a => chat.MessagesAsync(a, id, before, ct));
    [HttpPost("conversations"), Authorize(Policy = SupportChatPolicies.Resident), EnableRateLimiting(SupportChatPolicies.RateLimit), RequestSizeLimit(40000)]
    public Task<ActionResult<SupportConversationView>> Start(ChatSendRequest request, CancellationToken ct) => Execute(a => chat.StartAsync(a, request, ct));
    [HttpPost("conversations/{id:guid}/messages"), EnableRateLimiting(SupportChatPolicies.RateLimit), RequestSizeLimit(40000)]
    public Task<ActionResult<SupportMessageView>> Send(Guid id, ChatSendRequest request, CancellationToken ct) => Execute(a => chat.SendAsync(a, id, request, ct));
    [HttpPost("conversations/{id:guid}/close"), EnableRateLimiting(SupportChatPolicies.RateLimit)]
    public Task<ActionResult<SupportConversationView>> Close(Guid id, CancellationToken ct) => Execute(a => chat.CloseAsync(a, id, ct));
    [HttpGet("availability"), Authorize(Policy = SupportChatPolicies.Manager)]
    public Task<ActionResult<SupportAvailabilityView>> Availability(CancellationToken ct) => Execute(a => chat.AvailabilityAsync(a, ct));
    [HttpPut("availability"), Authorize(Policy = SupportChatPolicies.Manager), EnableRateLimiting(SupportChatPolicies.RateLimit)]
    public Task<ActionResult<SupportAvailabilityView>> SetAvailability(ChatAvailabilityRequest request, CancellationToken ct) => Execute(a => chat.SetAvailabilityAsync(a, request.Available, ct));
    private async Task<ActionResult<T>> Execute<T>(Func<Guid, Task<T>> action)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var actor)) return Unauthorized();
        try { return Ok(await action(actor)); }
        catch (SupportChatFailure error) { return StatusCode(error.Status, new ProblemDetails {
            Status = error.Status, Title = error.Message, Extensions = { ["code"] = error.Code, ["traceId"] = HttpContext.TraceIdentifier } }); }
    }
}
