using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.Administration.Presentation;

[ApiController, Route("api/v1/administration/system-activity")]
[Authorize(Roles = SystemRoleCodes.Admin, Policy = AdministrationAuthorizationPolicies.ViewAdministrationActivity)]
public sealed class SystemActivityController(AdministrationDbContext db, IInternalAccountDirectory accounts,
    IAuditActorLookup actors, ICurrentBuildingTimeZone buildingTimeZone) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedAdministrationActivitiesResponse>> Get(string? module = null, string? action = null,
        string? outcome = null, string? actor = null, DateOnly? dateFrom = null, DateOnly? dateTo = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (dateFrom > dateTo || dateTo == DateOnly.MaxValue)
            return BadRequest(new ProblemDetails { Title = "Khoảng ngày lọc không hợp lệ.", Status = 400 });
        if (actor?.Length > 100 || module?.Length > 50 || action?.Length > 100)
            return BadRequest(new ProblemDetails { Title = "Bộ lọc vượt quá độ dài cho phép.", Status = 400 });
        if (outcome is not (null or "" or "SUCCESS" or "FAILED" or "DENIED"))
            return BadRequest(new ProblemDetails { Title = "Kết quả lọc không hợp lệ.", Status = 400 });
        pageSize = Math.Clamp(pageSize, 1, 100); page = Math.Max(1, page);
        var timeZoneId = await buildingTimeZone.GetAsync(ct);
        var query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(module))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { Module = module });
            query = query.Where(x => EF.Functions.JsonContains(x.NewValues!, json) || module == "Administration" && x.EntityType == "InternalAccount");
        }
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action);
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { Outcome = outcome });
            query = query.Where(x => EF.Functions.JsonContains(x.NewValues!, json) || outcome == "SUCCESS" && x.EntityType == "InternalAccount");
        }
        if (!string.IsNullOrWhiteSpace(actor))
        {
            var ids = Guid.TryParse(actor, out var id) ? new[] { id } : (await actors.FindIdsAsync(actor, ct)).ToArray();
            query = query.Where(x => x.ActorUserId.HasValue && ids.Contains(x.ActorUserId.Value));
        }
        if (dateFrom.HasValue || dateTo.HasValue)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            if (dateFrom.HasValue)
            {
                var from = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dateFrom.Value.ToDateTime(TimeOnly.MinValue), zone));
                query = query.Where(x => x.CreatedAt >= from);
            }
            if (dateTo.HasValue)
            {
                var to = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
                query = query.Where(x => x.CreatedAt < to);
            }
        }
        var total = await query.CountAsync(ct);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var logs = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var idsToResolve = logs.SelectMany(x => x.EntityType == "InternalAccount" ? new[] { x.ActorUserId, x.EntityId } : new[] { x.ActorUserId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var identities = (await accounts.GetAsync(idsToResolve, ct)).ToDictionary(x => x.Id);
        var targetIds = logs.Where(x => x.EntityType == "InternalAccount" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).ToArray();
        var roles = await db.UserRoleAssignments.AsNoTracking().Include(x => x.Role).Where(x => targetIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.Role.Code, ct);
        return Ok(new PagedAdministrationActivitiesResponse(total, page, pageSize, logs.Select(x => AdministrationController.MapActivity(x, identities, roles)).ToArray(), timeZoneId));
    }
}
