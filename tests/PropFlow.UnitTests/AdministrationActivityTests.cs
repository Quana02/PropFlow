using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Domain.AuditLogs;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Presentation;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace PropFlow.UnitTests;

public sealed class AdministrationActivityTests
{
    private sealed class Directory(params InternalAccountRecord[] initialRecords) : IInternalAccountDirectory
    {
        private readonly List<InternalAccountRecord> records = [.. initialRecords];
        public bool RejectCreateAsConflict { get; init; }
        public Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InternalAccountRecord>>(records.Where(record => ids.Contains(record.Id)).ToArray());
        public Task<PagedInternalAccountRecords> SearchAsync(IReadOnlyCollection<Guid> ids, string? search, string? status, int page, int pageSize, CancellationToken ct)
        {
            var items = records.Where(record => ids.Contains(record.Id)).ToArray();
            return Task.FromResult(new PagedInternalAccountRecords(items.Length, items));
        }
        public Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct)
        {
            if (RejectCreateAsConflict)
                throw new InternalAccountConflictException("Tên đăng nhập đã được sử dụng.");
            var record = new InternalAccountRecord(Guid.NewGuid(), request.Username, request.DisplayName, request.Email, "ACTIVE");
            records.Add(record);
            return Task.FromResult(record);
        }
        public Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct)
        {
            var index = records.FindIndex(x => x.Id == userId);
            if (index < 0) return Task.FromResult<InternalAccountRecord?>(null);
            records[index] = records[index] with { Status = status.Trim().ToUpperInvariant() };
            return Task.FromResult<InternalAccountRecord?>(records[index]);
        }
    }

    private sealed class Transaction : IAdministrationTransaction
    {
        public int Executions { get; private set; }
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
        {
            Executions++;
            await operation(ct);
        }
    }

    private static void Authenticate(AdministrationController controller, Guid actorId) =>
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", actorId.ToString())], "test"))
            }
        };

    [Fact]
    public async Task Create_ExecutesIdentityRoleAndAuditInsideAdministrationTransaction()
    {
        await using var db = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        var transaction = new Transaction();
        var controller = new AdministrationController(db, new Directory(), transaction);
        Authenticate(controller, Guid.NewGuid());

        await controller.Create(new CreateInternalAccountRequest("staff01", "Nhân viên", "staff01@example.test", "Password123!", SystemRoleCodes.Staff, null), default);

        Assert.Equal(1, transaction.Executions);
        Assert.Single(db.UserRoleAssignments);
        Assert.Single(db.UserAccessHistories);
        Assert.Single(db.AuditLogs);
    }

    [Fact]
    public async Task Create_ReturnsSpecificConflict_WhenUsernameOrEmailAlreadyExists()
    {
        await using var db = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        var controller = new AdministrationController(db, new Directory { RejectCreateAsConflict = true }, new Transaction());
        Authenticate(controller, Guid.NewGuid());

        var response = await controller.Create(
            new CreateInternalAccountRequest("staff01", "Nhân viên", "staff01@example.test", "Password123!", SystemRoleCodes.Staff, null), default);

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(409, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Tên đăng nhập đã được sử dụng.", problem.Title);
        Assert.Equal("internal_account_conflict", problem.Extensions["code"]);
        Assert.Empty(db.UserRoleAssignments);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task AccountCommands_ReturnSpecificProblems_ForInvalidOrMissingTargets()
    {
        await using var db = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        var controller = new AdministrationController(db, new Directory(), new Transaction());
        Authenticate(controller, Guid.NewGuid());

        var invalidStatus = Assert.IsType<ObjectResult>(await controller.ChangeStatus(
            Guid.NewGuid(), new ChangeStatusRequest("UNKNOWN"), default));
        Assert.Equal(400, invalidStatus.StatusCode);
        Assert.Equal("Trạng thái mới phải là ACTIVE, DISABLED hoặc LOCKED.",
            Assert.IsType<ProblemDetails>(invalidStatus.Value).Title);

        var missingRoleTarget = Assert.IsType<ObjectResult>(await controller.ChangeRole(
            Guid.NewGuid(), new ChangeRoleRequest(SystemRoleCodes.Staff), default));
        Assert.Equal(404, missingRoleTarget.StatusCode);
        Assert.Equal("Không tìm thấy tài khoản Ban quản lý cần đổi vai trò.",
            Assert.IsType<ProblemDetails>(missingRoleTarget.Value).Title);

        var missingAccess = await controller.EffectiveAccess(Guid.NewGuid(), default);
        var missingAccessResult = Assert.IsType<ObjectResult>(missingAccess.Result);
        Assert.Equal(404, missingAccessResult.StatusCode);
        Assert.Equal("Không tìm thấy thông tin quyền của tài khoản Ban quản lý này.",
            Assert.IsType<ProblemDetails>(missingAccessResult.Value).Title);
    }

    [Fact]
    public async Task RolesAndEffectiveAccess_AreReadOnlyFixedRbacProjections()
    {
        await using var db = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        var staffRole = await db.Roles.SingleAsync(x => x.Code == SystemRoleCodes.Staff);
        var userId = Guid.NewGuid();
        db.UserRoleAssignments.Add(new UserRoleAssignment(userId, staffRole.Id, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var controller = new AdministrationController(db,
            new Directory(new InternalAccountRecord(userId, "staff01", "Nhân viên", "staff01@example.test", "ACTIVE")),
            new Transaction());

        var rolesResponse = await controller.Roles(default);
        var roles = Assert.IsType<RoleAccessResponse[]>(Assert.IsType<OkObjectResult>(rolesResponse.Result).Value);
        Assert.Equal(5, roles.Length);
        Assert.Equal(3, roles.Single(x => x.Code == SystemRoleCodes.Admin).Permissions.Count);

        var accessResponse = await controller.EffectiveAccess(userId, default);
        var access = Assert.IsType<InternalAccountAccessResponse>(Assert.IsType<OkObjectResult>(accessResponse.Result).Value);
        Assert.Equal(SystemRoleCodes.Staff, access.Role);
        Assert.Single(access.Permissions);
    }

    [Fact]
    public async Task List_All_ReturnsEveryInternalRoleWithItsActualRole()
    {
        await using var db = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        var managerRole = await db.Roles.SingleAsync(x => x.Code == SystemRoleCodes.Manager);
        var staffRole = await db.Roles.SingleAsync(x => x.Code == SystemRoleCodes.Staff);
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        db.UserRoleAssignments.AddRange(
            new UserRoleAssignment(managerId, managerRole.Id, DateTimeOffset.UtcNow),
            new UserRoleAssignment(staffId, staffRole.Id, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var controller = new AdministrationController(db, new Directory(
            new InternalAccountRecord(managerId, "manager1", "Quản lý", "manager@example.test", "ACTIVE"),
            new InternalAccountRecord(staffId, "staff1", "Nhân viên", "staff@example.test", "ACTIVE")),
            new Transaction());

        var response = await controller.List("ALL", null, null, 1, 20, default);
        var result = Assert.IsType<PagedInternalAccountsResponse>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(2, result.Total);
        Assert.Contains(result.Items, x => x.Id == managerId && x.Role == SystemRoleCodes.Manager);
        Assert.Contains(result.Items, x => x.Id == staffId && x.Role == SystemRoleCodes.Staff);
    }

    [Fact]
    public async Task Activity_projects_safe_typed_metadata_and_paginates()
    {
        await using var db = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 23, 0, 48, 0, TimeSpan.Zero);
        db.AuditLogs.AddRange(
            new AuditLog("internal_account_created", "InternalAccount", now.AddMinutes(-2), actorId, targetId, newValues: "{\"Username\":\"manager1\",\"Role\":\"MANAGER\"}"),
            new AuditLog("internal_account_role_changed", "InternalAccount", now.AddMinutes(-1), actorId, targetId, newValues: "{\"Username\":\"manager1\",\"OldRole\":\"STAFF\",\"NewRole\":\"MANAGER\"}"),
            new AuditLog("internal_account_status_changed", "InternalAccount", now, actorId, targetId, newValues: "{\"Username\":\"manager1\",\"OldStatus\":\"ACTIVE\",\"NewStatus\":\"DISABLED\"}"));
        await db.SaveChangesAsync();
        var directory = new Directory(
            new(actorId, "admin01", "Quản trị viên", "admin@example.test", "ACTIVE"),
            new(targetId, "manager1", "Quản lý 1", "manager@example.test", "DISABLED"));
        var controller = new AdministrationController(db, directory, new Transaction());

        var response = await controller.Activity(page: 1, pageSize: 2, ct: default);
        var page = Assert.IsType<PagedAdministrationActivitiesResponse>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Items.Count);
        var status = page.Items[0];
        Assert.Equal("admin01", status.ActorUsername);
        Assert.Equal("manager1", status.TargetUsername);
        Assert.Equal("ACTIVE", status.OldStatus);
        Assert.Equal("DISABLED", status.NewStatus);
        var role = page.Items[1];
        Assert.Equal("STAFF", role.OldRole);
        Assert.Equal("MANAGER", role.NewRole);
        Assert.DoesNotContain(typeof(AdministrationActivityResponse).GetProperties(), property => property.Name == "Details");
    }

    [Fact]
    public async Task Activity_action_filter_is_server_side()
    {
        await using var db = new AdministrationDbContext(new DbContextOptionsBuilder<AdministrationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AuditLogs.Add(new AuditLog("internal_account_created", "InternalAccount", DateTimeOffset.UtcNow, entityId: Guid.NewGuid(), newValues: "{\"Username\":\"staff01\",\"Role\":\"STAFF\"}"));
        db.AuditLogs.Add(new AuditLog("internal_account_role_changed", "InternalAccount", DateTimeOffset.UtcNow, entityId: Guid.NewGuid(), newValues: "{}"));
        await db.SaveChangesAsync();
        var controller = new AdministrationController(db, new Directory(), new Transaction());

        var response = await controller.Activity("internal_account_created", 1, 20, default);
        var page = Assert.IsType<PagedAdministrationActivitiesResponse>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(1, page.Total);
        Assert.Equal("internal_account_created", Assert.Single(page.Items).Action);
    }
}
