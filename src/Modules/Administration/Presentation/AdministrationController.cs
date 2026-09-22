using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Domain.AuditLogs;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.UserAccessHistories;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Administration.Presentation;

[ApiController, Route("api/v1/administration"), Authorize(Roles = SystemRoleCodes.Admin)]
[Produces("application/json")]
public sealed class AdministrationController(AdministrationDbContext db, IInternalAccountDirectory accounts) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private static readonly string[] InternalRoles = [SystemRoleCodes.Manager, SystemRoleCodes.Staff, SystemRoleCodes.Accountant];

    [HttpGet("internal-accounts/summary")]
    public async Task<ActionResult<InternalAccountSummaryResponse>> Summary(CancellationToken ct)
    {
        var assignments = await db.UserRoleAssignments.AsNoTracking().Include(x => x.Role)
            .Where(x => InternalRoles.Contains(x.Role.Code)).ToArrayAsync(ct);
        var users = await accounts.GetAsync(assignments.Select(x => x.UserId).ToArray(), ct);
        return Ok(new InternalAccountSummaryResponse(users.Count, users.Count(x => x.Status == "ACTIVE"), users.Count(x => x.Status == "DISABLED"), users.Count(x => x.Status == "LOCKED"),
            assignments.GroupBy(x => x.Role.Code).ToDictionary(x => x.Key, x => x.Count())));
    }

    [HttpGet("internal-accounts")]
    public async Task<ActionResult<PagedInternalAccountsResponse>> List([FromQuery] string role, [FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        role = SystemRoleCodes.Normalize(role);
        if (!InternalRoles.Contains(role)) return BadRequest(new ProblemDetails { Title = "Vai trò tài khoản nội bộ không hợp lệ." });
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var ids = await db.UserRoleAssignments.AsNoTracking().Include(x => x.Role).Where(x => x.Role.Code == role).Select(x => x.UserId).ToArrayAsync(ct);
        var rows = await accounts.GetAsync(ids, ct);
        var filtered = rows.Where(x => (string.IsNullOrWhiteSpace(search) || x.Username.Contains(search, StringComparison.OrdinalIgnoreCase) || x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) || (x.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)) && (string.IsNullOrWhiteSpace(status) || x.Status == status.Trim().ToUpperInvariant())).OrderBy(x => x.DisplayName).ToArray();
        return Ok(new PagedInternalAccountsResponse(filtered.Length, page, pageSize, filtered.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new InternalAccountResponse(x.Id, x.Username, x.DisplayName, x.Email, role, x.Status)).ToArray()));
    }

    [HttpPost("internal-accounts")]
    public async Task<ActionResult<InternalAccountResponse>> Create(CreateInternalAccountRequest request, CancellationToken ct)
    {
        var role = SystemRoleCodes.Normalize(request.Role);
        if (!InternalRoles.Contains(role)) return BadRequest(new ProblemDetails { Title = "Chỉ có thể tạo tài khoản MANAGER, STAFF hoặc ACCOUNTANT." });
        var account = await accounts.CreateAsync(new(request.Username, request.DisplayName, request.Email, request.Password, request.PhoneNumber), ActorId, ct);
        var roleEntity = await db.Roles.SingleAsync(x => x.Code == role && x.IsActive, ct);
        db.UserRoleAssignments.Add(new UserRoleAssignment(account.Id, roleEntity.Id, DateTimeOffset.UtcNow, ActorId));
        AddHistory(account.Id, AccessActionType.CREATED, newRole: roleEntity.Id);
        AddAudit("internal_account_created", account.Id, new { account.Username, Role = role });
        await db.SaveChangesAsync(ct);
        return Created($"api/v1/administration/internal-accounts/{account.Id}", new InternalAccountResponse(account.Id, account.Username, account.DisplayName, account.Email, role, account.Status));
    }

    [HttpPut("internal-accounts/{userId:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid userId, ChangeRoleRequest request, CancellationToken ct)
    {
        var target = SystemRoleCodes.Normalize(request.Role);
        if (!InternalRoles.Contains(target)) return BadRequest(new ProblemDetails { Title = "Vai trò mới không hợp lệ." });
        var assignment = await db.UserRoleAssignments.Include(x => x.Role).SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (assignment is null || !InternalRoles.Contains(assignment.Role.Code)) return NotFound();
        var role = await db.Roles.SingleAsync(x => x.Code == target && x.IsActive, ct);
        if (assignment.RoleId == role.Id) return NoContent();
        var account = (await accounts.GetAsync([userId], ct)).SingleOrDefault();
        var oldRoleId = assignment.RoleId;
        assignment.ChangeRole(role.Id, ActorId, DateTimeOffset.UtcNow);
        AddHistory(userId, AccessActionType.ROLE_CHANGED, oldRoleId, role.Id);
        AddAudit("internal_account_role_changed", userId, new { account?.Username, OldRole = assignment.Role.Code, NewRole = target });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("internal-accounts/{userId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid userId, ChangeStatusRequest request, CancellationToken ct)
    {
        var assignment = await db.UserRoleAssignments.Include(x => x.Role).SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (assignment is null || !InternalRoles.Contains(assignment.Role.Code)) return NotFound();
        var previous = (await accounts.GetAsync([userId], ct)).SingleOrDefault();
        var result = await accounts.SetStatusAsync(userId, request.Status, ActorId, ct);
        if (result is null) return NotFound();
        var action = request.Status.Trim().ToUpperInvariant() switch { "LOCKED" => AccessActionType.LOCKED, "DISABLED" => AccessActionType.DISABLED, _ => AccessActionType.RE_ENABLED };
        AccountStatus? oldStatus = previous is null ? null : Enum.Parse<AccountStatus>(previous.Status);
        var newStatus = Enum.Parse<AccountStatus>(result.Status);
        AddHistory(userId, action, oldStatus: oldStatus, newStatus: newStatus);
        AddAudit("internal_account_status_changed", userId, new { result.Username, OldStatus = previous?.Status, NewStatus = result.Status });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("activity")]
    public async Task<ActionResult<PagedAdministrationActivitiesResponse>> Activity([FromQuery] string? action = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.AuditLogs.AsNoTracking().Where(x => x.EntityType == "InternalAccount");
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action.Trim());
        var total = await query.CountAsync(ct);
        var events = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var identityIds = events.SelectMany(x => new[] { x.ActorUserId, x.EntityId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var identities = (await accounts.GetAsync(identityIds, ct)).ToDictionary(x => x.Id);
        var targetIds = events.Where(x => x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToArray();
        var roles = await db.UserRoleAssignments.AsNoTracking().Include(x => x.Role).Where(x => targetIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.Role.Code, ct);
        var items = events.Select(log => MapActivity(log, identities, roles)).ToArray();
        return Ok(new PagedAdministrationActivitiesResponse(total, page, pageSize, items));
    }

    private static AdministrationActivityResponse MapActivity(AuditLog log, IReadOnlyDictionary<Guid, InternalAccountRecord> identities, IReadOnlyDictionary<Guid, string> roles)
    {
        ActivityMetadata metadata;
        try { metadata = JsonSerializer.Deserialize<ActivityMetadata>(log.NewValues ?? "{}") ?? new(); }
        catch (JsonException) { metadata = new(); }
        identities.TryGetValue(log.ActorUserId ?? Guid.Empty, out var actor);
        identities.TryGetValue(log.EntityId ?? Guid.Empty, out var target);
        roles.TryGetValue(log.EntityId ?? Guid.Empty, out var currentRole);
        return new(log.Id, log.CreatedAt, log.Action,
            log.ActorUserId, actor?.Username, actor?.DisplayName,
            log.EntityId, target?.Username ?? metadata.Username, target?.DisplayName,
            metadata.Role ?? metadata.NewRole ?? currentRole, target?.Status,
            metadata.OldRole, metadata.NewRole, metadata.OldStatus, metadata.NewStatus ?? metadata.Status);
    }

    private void AddHistory(Guid userId, AccessActionType action, Guid? oldRole = null, Guid? newRole = null, AccountStatus? oldStatus = null, AccountStatus? newStatus = null) => db.UserAccessHistories.Add(new UserAccessHistory(userId, action, DateTimeOffset.UtcNow, oldRole, newRole, oldStatus, newStatus, performedBy: ActorId));
    private void AddAudit(string action, Guid userId, object values) => db.AuditLogs.Add(new AuditLog(action, "InternalAccount", DateTimeOffset.UtcNow, ActorId, userId, newValues: JsonSerializer.Serialize(values), ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), correlationId: HttpContext.TraceIdentifier));
}

public sealed record ActivityMetadata(string? Username = null, string? Role = null, string? OldRole = null, string? NewRole = null, string? Status = null, string? OldStatus = null, string? NewStatus = null);

public sealed record InternalAccountSummaryResponse(int Total, int Active, int Disabled, int Locked, IReadOnlyDictionary<string, int> RoleCounts);
public sealed record PagedInternalAccountsResponse(int Total, int Page, int PageSize, IReadOnlyList<InternalAccountResponse> Items);
public sealed record InternalAccountResponse(Guid Id, string Username, string DisplayName, string? Email, string Role, string Status);
public sealed record CreateInternalAccountRequest([Required, StringLength(50, MinimumLength = 3)] string Username, [Required, StringLength(150)] string DisplayName, [Required, EmailAddress] string Email, [Required, StringLength(128, MinimumLength = 12)] string Password, [Required] string Role, string? PhoneNumber);
public sealed record ChangeRoleRequest([Required] string Role);
public sealed record ChangeStatusRequest([Required] string Status);
public sealed record PagedAdministrationActivitiesResponse(int Total, int Page, int PageSize, IReadOnlyList<AdministrationActivityResponse> Items);
public sealed record AdministrationActivityResponse(Guid Id, DateTimeOffset Timestamp, string Action,
    Guid? ActorUserId, string? ActorUsername, string? ActorDisplayName,
    Guid? TargetAccountId, string? TargetUsername, string? TargetDisplayName, string? TargetRole, string? TargetStatus,
    string? OldRole, string? NewRole, string? OldStatus, string? NewStatus);
