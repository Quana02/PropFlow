using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Domain;

namespace PropFlow.Modules.AiChatbot.Presentation;

[ApiController, Route("api/v1/resident/chatbot"), Authorize(Policy = ResidentChatPolicy.Use)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResidentChatController(ResidentChat chat, IChatAi ai) : ControllerBase
{
    [HttpGet("status")]
    public Task<ActionResult<ResidentChatStatus>> Status(CancellationToken ct) => Execute(async actor =>
    { await chat.EnsureAccess(actor, ct); return new ResidentChatStatus(ai.Ready); });

    [HttpPost("ask"), EnableRateLimiting(ResidentChatPolicy.RateLimit), RequestSizeLimit(12000)]
    public Task<ActionResult<ResidentChatReply>> Ask(AskResidentChat input, CancellationToken ct) =>
        Execute(actor => chat.AskAsync(actor, input.Question, ct));

    [HttpGet("sources/{id:guid}/{revision:int}")]
    public async Task<ActionResult<PublishedKnowledgeSource>> Source(Guid id, int revision, CancellationToken ct)
    {
        var actor = Actor(); if (actor == Guid.Empty) return Unauthorized();
        try
        {
            var source = await chat.SourceAsync(actor, id, revision, ct);
            return source is null ? Failure(404, "source_unavailable", "Tài liệu đã thay đổi, bị thu hồi hoặc không được cung cấp cho cư dân.") : Ok(source);
        }
        catch (ChatAccessException) { return Forbidden(); }
    }
    private Guid Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var actor) ? actor : Guid.Empty;
    private async Task<ActionResult<T>> Execute<T>(Func<Guid, Task<T>> operation)
    {
        var actor = Actor(); if (actor == Guid.Empty) return Unauthorized();
        try { return Ok(await operation(actor)); }
        catch (ChatAccessException) { return Forbidden(); }
        catch (KnowledgeValidationException e) { return Failure(400, "chat_question_invalid", e.Message); }
    }
    private ObjectResult Forbidden() => Failure(403, "resident_scope_missing", "Tài khoản chưa có liên kết cư trú đang hiệu lực để dùng trợ lý.");
    private ObjectResult Failure(int status, string code, string message) => StatusCode(status,
        new ProblemDetails { Status = status, Title = message, Extensions = { ["code"] = code, ["traceId"] = HttpContext.TraceIdentifier } });
}
