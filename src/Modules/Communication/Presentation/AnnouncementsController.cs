using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Communication.Application.Announcements;

namespace PropFlow.Modules.Communication.Presentation;

[ApiController, Route("api/v1/announcements"), Authorize(Policy = "announcements.access")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
public sealed class AnnouncementsController(AnnouncementUseCases useCases, IAuthorizationService authorization, IHubContext<AnnouncementHub> hub, ILogger<AnnouncementsController> logger) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet]
    [ProducesResponseType(typeof(AnnouncementPage), 200)]
    public async Task<IActionResult> List(int page = 1, CancellationToken ct = default) => Map(await useCases.ListAsync(Actor,
        (await authorization.AuthorizeAsync(User, "announcements.publish")).Succeeded, page, ct));
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnnouncementView), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Map(await useCases.GetAsync(Actor,
        (await authorization.AuthorizeAsync(User, "announcements.publish")).Succeeded, id, ct));
    [HttpPost, Authorize(Policy = "announcements.publish")]
    [ProducesResponseType(typeof(AnnouncementView), 201)]
    public async Task<IActionResult> Publish(PublishAnnouncement request, CancellationToken ct)
    {
        var result = await useCases.PublishAsync(Actor, request, ct);
        if (result.Status == 201)
        {
            try { await hub.Clients.All.SendAsync("AnnouncementPublished", Guid.Empty, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Announcement was saved, but realtime invalidation failed."); }
        }
        return Map(result);
    }
    private IActionResult Map<T>(AnnouncementResult<T> result)
    {
        if (result.Status < 400) return StatusCode(result.Status, result.Data);
        var problem = new ProblemDetails { Status = result.Status, Title = "Không thể thực hiện thao tác thông báo", Detail = result.Message };
        problem.Extensions["code"] = $"announcement_{result.Status}";
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(result.Status, problem);
    }
}
