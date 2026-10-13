using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
namespace PropFlow.Modules.Communication.Presentation;
[ApiController, Route("api/v1/resident/notifications"), Authorize(Roles = "RESIDENT")]
public sealed class ResidentNotificationsController(CommunicationDbContext db) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.Notifications.AsNoTracking()
        .Where(x => x.RecipientUserId == Actor).OrderByDescending(x => x.CreatedAt).Take(100)
        .Select(x => new { x.Id, x.Title, x.Message, x.ActionPath, x.IsRead, x.CreatedAt }).ToArrayAsync(ct));
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.RecipientUserId == Actor, ct);
        if (n is null) return NotFound(); n.MarkAsRead(DateTimeOffset.UtcNow); await db.SaveChangesAsync(ct); return Ok(new { read = true });
    }
}
