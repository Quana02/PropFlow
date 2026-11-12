using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    [ProducesResponseType(typeof(SupportMessageView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<SupportMessageView>> Send(Guid id, ChatSendRequest request, CancellationToken ct) => Execute(a => chat.SendAsync(a, id, request, ct));
    [HttpGet("conversations/{id:guid}/messages/{messageId:guid}")]
    [ProducesResponseType(typeof(SupportMessageView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<SupportMessageView>> Message(Guid id, Guid messageId, CancellationToken ct) =>
        Execute(a => chat.MessageAsync(a, id, messageId, ct));
    [HttpPut("conversations/{id:guid}/messages/{messageId:guid}/recall"), EnableRateLimiting(SupportChatPolicies.RateLimit)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<bool>> Recall(Guid id, Guid messageId, CancellationToken ct) =>
        Execute(a => chat.RecallAsync(a, id, messageId, ct));
    [HttpPut("conversations/{id:guid}/messages/recall"), EnableRateLimiting(SupportChatPolicies.RateLimit), RequestSizeLimit(12000)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<bool>> RecallMany(Guid id, ChatMessageSelection request, CancellationToken ct) =>
        Execute(a => chat.RecallManyAsync(a, id, request, ct));
    [HttpPost("conversations/{id:guid}/messages/hide"), EnableRateLimiting(SupportChatPolicies.RateLimit), RequestSizeLimit(12000)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<bool>> Hide(Guid id, ChatMessageSelection request, CancellationToken ct) =>
        Execute(a => chat.HideAsync(a, id, request, ct));
    [HttpPost("conversations/{id:guid}/messages/receipts"), EnableRateLimiting(SupportChatPolicies.ReceiptRateLimit), RequestSizeLimit(12000)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<bool>> Acknowledge(Guid id, ChatMessageReceipt request, CancellationToken ct) =>
        Execute(a => chat.AcknowledgeAsync(a, id, request, ct));
    [HttpPost("conversations/{id:guid}/messages/forward"), EnableRateLimiting(SupportChatPolicies.RateLimit), RequestSizeLimit(12000)]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<int>> Forward(Guid id, ChatForwardRequest request, CancellationToken ct) =>
        Execute(a => chat.ForwardAsync(a, id, request, ct));
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
