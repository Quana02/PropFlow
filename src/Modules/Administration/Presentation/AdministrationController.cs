using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Domain.AuditLogs;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.UserAccessHistories;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Administration.Contracts;

namespace PropFlow.Modules.Administration.Presentation;

[ApiController, Route("api/v1/administration"), Authorize(Roles = SystemRoleCodes.Admin)]
[Produces("application/json")]
public sealed class AdministrationController(
    AdministrationDbContext db,
    IInternalAccountDirectory accounts,
    IAdministrationTransaction transaction) : ControllerBase
{
    private Guid ActorId => Guid.Parse(User.FindFirstValue("sub")!);
    private static readonly string[] InternalRoles = [SystemRoleCodes.Manager, SystemRoleCodes.Staff, SystemRoleCodes.Accountant];
    private static readonly string[] InternalAccountStatuses = ["ACTIVE", "DISABLED", "LOCKED"];

    private static ObjectResult Problem(int status, string title, string code) => new(new ProblemDetails
    {
        Status = status,
        Title = title,
        Extensions = { ["code"] = code }
    }) { StatusCode = status };

    [HttpGet("internal-accounts/summary")]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ManageInternalAccounts)]
    public async Task<ActionResult<InternalAccountSummaryResponse>> Summary(CancellationToken ct)
    {
        var assignments = await db.UserRoleAssignments.AsNoTracking().Include(x => x.Role)
            .Where(x => InternalRoles.Contains(x.Role.Code)).ToArrayAsync(ct);
        var users = await accounts.GetAsync(assignments.Select(x => x.UserId).ToArray(), ct);
        var existingUserIds = users.Select(x => x.Id).ToHashSet();
        return Ok(new InternalAccountSummaryResponse(users.Count, users.Count(x => x.Status == "ACTIVE"), users.Count(x => x.Status == "DISABLED"), users.Count(x => x.Status == "LOCKED"),
            assignments.Where(x => existingUserIds.Contains(x.UserId)).GroupBy(x => x.Role.Code).ToDictionary(x => x.Key, x => x.Count())));
    }

    [HttpGet("internal-accounts")]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ManageInternalAccounts)]
    public async Task<ActionResult<PagedInternalAccountsResponse>> List([FromQuery] string role, [FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        role = SystemRoleCodes.Normalize(role);
        var allRoles = role == "ALL";
        if (!allRoles && !InternalRoles.Contains(role)) return Problem(400, "Vai trò lọc không hợp lệ.", "invalid_internal_role");
        if (!string.IsNullOrWhiteSpace(status) && !InternalAccountStatuses.Contains(status.Trim().ToUpperInvariant()))
            return Problem(400, "Trạng thái lọc không hợp lệ.", "invalid_account_status");
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var assignmentQuery = db.UserRoleAssignments.AsNoTracking().Include(x => x.Role)
            .Where(x => InternalRoles.Contains(x.Role.Code));
        if (!allRoles) assignmentQuery = assignmentQuery.Where(x => x.Role.Code == role);
        var assignments = await assignmentQuery.Select(x => new { x.UserId, Role = x.Role.Code }).ToArrayAsync(ct);
        var roleByUserId = assignments.GroupBy(x => x.UserId).ToDictionary(x => x.Key, x => x.First().Role);
        var ids = roleByUserId.Keys.ToArray();
        var result = await accounts.SearchAsync(ids, search, status, page, pageSize, ct);
        return Ok(new PagedInternalAccountsResponse(result.Total, page, pageSize,
            result.Items.Select(x => new InternalAccountResponse(x.Id, x.Username, x.DisplayName, x.Email, roleByUserId[x.Id], x.Status)).ToArray()));
    }

    [HttpPost("internal-accounts")]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ManageInternalAccounts)]
    public async Task<ActionResult<InternalAccountResponse>> Create(CreateInternalAccountRequest request, CancellationToken ct)
    {
        var role = SystemRoleCodes.Normalize(request.Role);
        if (!InternalRoles.Contains(role)) return Problem(400, "Chỉ có thể tạo tài khoản MANAGER, STAFF hoặc ACCOUNTANT.", "invalid_internal_role");
        InternalAccountRecord? account = null;
        try
        {
            await transaction.ExecuteAsync(async innerCt =>
            {
                account = await accounts.CreateAsync(new(request.Username, request.DisplayName, request.Email, request.Password, request.PhoneNumber), ActorId, innerCt);
                var roleEntity = await db.Roles.SingleAsync(x => x.Code == role && x.IsActive, innerCt);
                db.UserRoleAssignments.Add(new UserRoleAssignment(account.Id, roleEntity.Id, DateTimeOffset.UtcNow, ActorId));
                AddHistory(account.Id, AccessActionType.CREATED, newRole: roleEntity.Id);
                AddAudit("internal_account_created", account.Id, new { account.Username, Role = role });
                await db.SaveChangesAsync(innerCt);
            }, ct);
        }
        catch (InternalAccountConflictException exception)
        {
            return Problem(StatusCodes.Status409Conflict, exception.Message, "internal_account_conflict");
        }
        ArgumentNullException.ThrowIfNull(account);
        return Created($"api/v1/administration/internal-accounts/{account.Id}", new InternalAccountResponse(account.Id, account.Username, account.DisplayName, account.Email, role, account.Status));
    }

    [HttpPut("internal-accounts/{userId:guid}/role")]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ManageInternalAccounts)]
    public async Task<IActionResult> ChangeRole(Guid userId, ChangeRoleRequest request, CancellationToken ct)
    {
        var target = SystemRoleCodes.Normalize(request.Role);
        if (!InternalRoles.Contains(target)) return Problem(400, "Vai trò mới phải là MANAGER, STAFF hoặc ACCOUNTANT.", "invalid_internal_role");
        var assignment = await db.UserRoleAssignments.Include(x => x.Role).SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (assignment is null || !InternalRoles.Contains(assignment.Role.Code))
            return Problem(404, "Không tìm thấy tài khoản Ban quản lý cần đổi vai trò.", "internal_account_not_found");
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Code == target && x.IsActive, ct);
        if (role is null) return Problem(409, "Vai trò được chọn hiện không khả dụng.", "role_unavailable");
        if (assignment.RoleId == role.Id) return Problem(409, "Tài khoản đang có vai trò này.", "role_unchanged");
        var account = (await accounts.GetAsync([userId], ct)).SingleOrDefault();
        var oldRoleId = assignment.RoleId;
        assignment.ChangeRole(role.Id, ActorId, DateTimeOffset.UtcNow);
        AddHistory(userId, AccessActionType.ROLE_CHANGED, oldRoleId, role.Id);
        AddAudit("internal_account_role_changed", userId, new { account?.Username, OldRole = assignment.Role.Code, NewRole = target });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("internal-accounts/{userId:guid}/status")]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ManageInternalAccounts)]
    public async Task<IActionResult> ChangeStatus(Guid userId, ChangeStatusRequest request, CancellationToken ct)
    {
        var targetStatus = request.Status.Trim().ToUpperInvariant();
        if (!InternalAccountStatuses.Contains(targetStatus))
            return Problem(400, "Trạng thái mới phải là ACTIVE, DISABLED hoặc LOCKED.", "invalid_account_status");
        var assignment = await db.UserRoleAssignments.Include(x => x.Role).SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (assignment is null || !InternalRoles.Contains(assignment.Role.Code))
            return Problem(404, "Không tìm thấy tài khoản Ban quản lý cần cập nhật trạng thái.", "internal_account_not_found");
        var previous = (await accounts.GetAsync([userId], ct)).SingleOrDefault();
        if (previous is null)
            return Problem(404, "Tài khoản không còn tồn tại trong hệ thống xác thực.", "identity_account_not_found");
        if (string.Equals(previous.Status, targetStatus, StringComparison.OrdinalIgnoreCase))
            return Problem(409, $"Tài khoản đã ở trạng thái {targetStatus}.", "status_unchanged");
        InternalAccountRecord? result = null;
        await transaction.ExecuteAsync(async innerCt =>
        {
            result = await accounts.SetStatusAsync(userId, targetStatus, ActorId, innerCt);
            if (result is null) return;
            var action = result.Status switch { "LOCKED" => AccessActionType.LOCKED, "DISABLED" => AccessActionType.DISABLED, _ => AccessActionType.RE_ENABLED };
            AccountStatus? oldStatus = previous is null ? null : Enum.Parse<AccountStatus>(previous.Status);
            var newStatus = Enum.Parse<AccountStatus>(result.Status);
            AddHistory(userId, action, oldStatus: oldStatus, newStatus: newStatus);
            AddAudit("internal_account_status_changed", userId, new { result.Username, OldStatus = previous?.Status, NewStatus = result.Status });
            await db.SaveChangesAsync(innerCt);
        }, ct);
        if (result is null) return Problem(404, "Tài khoản không còn tồn tại trong hệ thống xác thực.", "identity_account_not_found");
        return NoContent();
    }

    [HttpGet("roles")]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ManageInternalAccounts)]
    public async Task<ActionResult<IReadOnlyList<RoleAccessResponse>>> Roles(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking()
            .Include(x => x.RolePermissions).ThenInclude(x => x.Permission)
            .OrderBy(x => x.Code).ToArrayAsync(ct);
        return Ok(roles.Select(role => new RoleAccessResponse(
            role.Code,
            role.Name,
            role.Description,
            role.IsActive,
            role.RolePermissions.Where(x => x.Permission.IsActive)
                .OrderBy(x => x.Permission.Module).ThenBy(x => x.Permission.Code)
                .Select(x => new PermissionResponse(x.Permission.Code, x.Permission.Name, x.Permission.Module, x.Permission.Description))
                .ToArray())).ToArray());
    }

    [HttpGet("internal-accounts/{userId:guid}/effective-access")]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ManageInternalAccounts)]
    public async Task<ActionResult<InternalAccountAccessResponse>> EffectiveAccess(Guid userId, CancellationToken ct)
    {
        var assignment = await db.UserRoleAssignments.AsNoTracking()
            .Include(x => x.Role).ThenInclude(x => x.RolePermissions).ThenInclude(x => x.Permission)
            .SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (assignment is null || !InternalRoles.Contains(assignment.Role.Code))
            return Problem(404, "Không tìm thấy thông tin quyền của tài khoản Ban quản lý này.", "internal_account_not_found");
        var account = (await accounts.GetAsync([userId], ct)).SingleOrDefault();
        if (account is null) return Problem(404, "Tài khoản không còn tồn tại trong hệ thống xác thực.", "identity_account_not_found");
        var permissions = assignment.Role.RolePermissions.Where(x => x.Permission.IsActive)
            .OrderBy(x => x.Permission.Module).ThenBy(x => x.Permission.Code)
            .Select(x => new PermissionResponse(x.Permission.Code, x.Permission.Name, x.Permission.Module, x.Permission.Description))
            .ToArray();
        return Ok(new InternalAccountAccessResponse(account.Id, account.Username, account.DisplayName, account.Status, assignment.Role.Code, permissions));
    }

    [HttpGet("activity")]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ViewAdministrationActivity)]
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
public sealed record CreateInternalAccountRequest(
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập."), StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập phải có từ 3 đến 50 ký tự."), RegularExpression(@"[a-zA-Z0-9_.-]+", ErrorMessage = "Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.")] string Username,
    [Required(ErrorMessage = "Vui lòng nhập họ và tên."), StringLength(150, ErrorMessage = "Họ và tên không được vượt quá 150 ký tự.")] string DisplayName,
    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email không đúng định dạng."), StringLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự.")] string Email,
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), StringLength(128, MinimumLength = 12, ErrorMessage = "Mật khẩu phải có từ 12 đến 128 ký tự.")] string Password,
    [Required(ErrorMessage = "Vui lòng chọn vai trò.")] string Role,
    [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")] string? PhoneNumber);
public sealed record ChangeRoleRequest([Required(ErrorMessage = "Vui lòng chọn vai trò mới.")] string Role);
public sealed record ChangeStatusRequest([Required(ErrorMessage = "Vui lòng chọn trạng thái mới.")] string Status);
public sealed record PermissionResponse(string Code, string Name, string Module, string? Description);
public sealed record RoleAccessResponse(string Code, string Name, string? Description, bool IsActive, IReadOnlyList<PermissionResponse> Permissions);
public sealed record InternalAccountAccessResponse(Guid UserId, string Username, string DisplayName, string Status, string Role, IReadOnlyList<PermissionResponse> Permissions);
public sealed record PagedAdministrationActivitiesResponse(int Total, int Page, int PageSize, IReadOnlyList<AdministrationActivityResponse> Items);
public sealed record AdministrationActivityResponse(Guid Id, DateTimeOffset Timestamp, string Action,
    Guid? ActorUserId, string? ActorUsername, string? ActorDisplayName,
    Guid? TargetAccountId, string? TargetUsername, string? TargetDisplayName, string? TargetRole, string? TargetStatus,
    string? OldRole, string? NewRole, string? OldStatus, string? NewStatus);
