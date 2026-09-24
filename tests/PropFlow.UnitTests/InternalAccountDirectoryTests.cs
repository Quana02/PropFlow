using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

public sealed class InternalAccountDirectoryTests
{
    [Fact]
    public async Task CreateAsync_ReportsUsernameConflictSeparatelyFromEmail()
    {
        await using var db = CreateContext();
        db.UserAccounts.Add(new UserAccount("manager1", "hash", "Quản lý", DateTimeOffset.UtcNow,
            "existing@example.test", status: AccountStatus.ACTIVE));
        await db.SaveChangesAsync();
        var directory = new InternalAccountDirectory(db, null!);

        var exception = await Assert.ThrowsAsync<InternalAccountConflictException>(() => directory.CreateAsync(
            new CreateInternalAccount("manager1", "Tên khác", "new-email@example.test", "Password123!", null),
            Guid.NewGuid(), default));

        Assert.Equal("Tên đăng nhập đã được sử dụng.", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_ReportsEmailConflictSeparatelyFromUsername()
    {
        await using var db = CreateContext();
        db.UserAccounts.Add(new UserAccount("manager1", "hash", "Quản lý", DateTimeOffset.UtcNow,
            "existing@example.test", status: AccountStatus.ACTIVE));
        await db.SaveChangesAsync();
        var directory = new InternalAccountDirectory(db, null!);

        var exception = await Assert.ThrowsAsync<InternalAccountConflictException>(() => directory.CreateAsync(
            new CreateInternalAccount("new-manager", "Tên khác", "existing@example.test", "Password123!", null),
            Guid.NewGuid(), default));

        Assert.Equal("Email đã được sử dụng.", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_FiltersAndPaginatesInOwnerModuleQuery()
    {
        await using var db = new AuthenticationDbContext(new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        var active = new UserAccount("staff01", "hash", "Nguyễn An", now, "staff01@example.test", status: AccountStatus.ACTIVE);
        var disabled = new UserAccount("staff02", "hash", "Nguyễn Bình", now, "staff02@example.test", status: AccountStatus.DISABLED);
        var unrelated = new UserAccount("manager01", "hash", "Trần Cường", now, "manager@example.test", status: AccountStatus.ACTIVE);
        db.UserAccounts.AddRange(active, disabled, unrelated);
        await db.SaveChangesAsync();
        var directory = new InternalAccountDirectory(db, null!);

        var result = await directory.SearchAsync(
            [active.Id, disabled.Id], "staff", "ACTIVE", page: 1, pageSize: 10, default);

        Assert.Equal(1, result.Total);
        Assert.Equal(active.Id, Assert.Single(result.Items).Id);
    }

    private static AuthenticationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
