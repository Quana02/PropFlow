using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Domain.AuditLogs;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Presentation;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.UnitTests;

public sealed class AdministrationActivityTests
{
    private sealed class Directory(params InternalAccountRecord[] records) : IInternalAccountDirectory
    {
        public Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InternalAccountRecord>>(records.Where(record => ids.Contains(record.Id)).ToArray());
        public Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
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
        var controller = new AdministrationController(db, directory);

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
        var controller = new AdministrationController(db, new Directory());

        var response = await controller.Activity("internal_account_created", 1, 20, default);
        var page = Assert.IsType<PagedAdministrationActivitiesResponse>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(1, page.Total);
        Assert.Equal("internal_account_created", Assert.Single(page.Items).Action);
    }
}
