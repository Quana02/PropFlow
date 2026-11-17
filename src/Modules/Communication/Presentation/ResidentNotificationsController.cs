using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
namespace PropFlow.Modules.Communication.Presentation;
[ApiController, Route("api/v1/resident/notifications"), Route("api/v1/notifications"), Authorize]
public sealed class ResidentNotificationsController(CommunicationDbContext db, IFinanceNotificationRealtime realtime,
    ILogger<ResidentNotificationsController> logger) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, int page = 1, int pageSize = 100)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            return BadRequest(new ProblemDetails { Status = 400, Title = "Tham số phân trang thông báo không hợp lệ." });
        return Ok(await db.Notifications.AsNoTracking().Where(x => x.RecipientUserId == Actor)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Title, x.Message, x.ActionPath, x.IsRead, x.CreatedAt, x.SourceType, x.SourceId })
            .ToArrayAsync(ct));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct) =>
        Ok(new { count = await db.Notifications.CountAsync(x => x.RecipientUserId == Actor && !x.IsRead, ct) });

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var actor = Actor;
        var now = DateTimeOffset.UtcNow;
        var updated = await db.Notifications.Where(x => x.RecipientUserId == actor && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAt, x => x.CreatedAt > now ? x.CreatedAt : now), ct);
        if (updated > 0) await NotifyReadStateChangedAsync(actor);
        return Ok(new { updated });
    }
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        var actor = Actor;
        var owned = db.Notifications.Where(x => x.Id == id && x.RecipientUserId == actor);
        var now = DateTimeOffset.UtcNow;
        var updated = await owned.Where(x => !x.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAt, x => x.CreatedAt > now ? x.CreatedAt : now), ct);
        if (updated > 0) await NotifyReadStateChangedAsync(actor);
        else if (!await owned.AnyAsync(ct)) return NotFound();
        return Ok(new { read = true });
    }

    private async Task NotifyReadStateChangedAsync(Guid actor)
    {
        try { await realtime.ReadStateChangedAsync(actor, CancellationToken.None); }
        catch (Exception error)
        {
            // The read state is already committed. Reconciliation will recover a missed signal.
            logger.LogWarning("Notification read-state signal could not be sent ({ErrorType}).", error.GetType().Name);
        }
    }
}
