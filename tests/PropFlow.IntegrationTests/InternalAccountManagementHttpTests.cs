using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Presentation;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-15")]
public sealed class InternalAccountManagementHttpTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    private const string Password = "Test passphrase 123!";
    private const string Path = "api/v1/administration/internal-accounts";

    private async Task<HttpClient> ClientAsync(string role)
    {
        using var scope = database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<IAuthSecrets>();
        var user = new UserAccount("fe15_" + Guid.NewGuid().ToString("N")[..12], "pending", "FE15 tester", DateTimeOffset.UtcNow,
            status: AccountStatus.ACTIVE, emailVerified: true);
        user.ChangePasswordHash(secrets.HashPassword(user, Password), null, DateTimeOffset.UtcNow);
        auth.UserAccounts.Add(user); await auth.SaveChangesAsync();
        var businessRole = await access.Roles.Include(x => x.RolePermissions).ThenInclude(x => x.Permission).SingleAsync(x => x.Code == role);
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, businessRole.Id, DateTimeOffset.UtcNow));
        await access.SaveChangesAsync();
        var account = new AccountResponse(user.Id, user.Username, user.DisplayName, null, null, "ACTIVE", true, role,
            businessRole.RolePermissions.Select(x => x.Permission.Code).ToArray());
        var client = database.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secrets.Issue(account, DateTimeOffset.UtcNow).AccessToken);
        return client;
    }

    private static CreateInternalAccountRequest Request(string role = "STAFF")
    {
        var token = Guid.NewGuid().ToString("N")[..12];
        return new("internal_" + token, "Internal tester", token + "@example.invalid", Password, role, null);
    }

    [Theory]
    [InlineData("ADMIN", HttpStatusCode.Created)]
    [InlineData("MANAGER", HttpStatusCode.Forbidden)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)]
    [InlineData("RESIDENT", HttpStatusCode.Forbidden)]
    public async Task Create_enforces_actor_matrix(string role, HttpStatusCode expected)
    {
        using var client = await ClientAsync(role);
        using var result = await client.PostAsJsonAsync(Path, Request());
        Assert.Equal(expected, result.StatusCode);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("RESIDENT")]
    [InlineData("CUSTOM")]
    public async Task Invalid_role_does_not_reserve_email(string role)
    {
        using var client = await ClientAsync("ADMIN");
        var request = Request(role);
        using var invalid = await client.PostAsJsonAsync(Path, request);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var retry = await client.PostAsJsonAsync(Path, request with { Role = "STAFF" });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_search_change_role_lock_disable_reenable_and_read_audit()
    {
        using var admin = await ClientAsync("ADMIN");
        var request = Request();
        using var created = await admin.PostAsJsonAsync(Path, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = (await created.Content.ReadFromJsonAsync<InternalAccountResponse>())!;
        using var duplicate = await admin.PostAsJsonAsync(Path, request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var listing = await admin.GetFromJsonAsync<PagedInternalAccountsResponse>($"{Path}?role=ALL&search={request.Username}");
        Assert.Single(listing!.Items); Assert.Equal(account.Id, listing.Items[0].Id);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Path}/{account.Id}/role", new ChangeRoleRequest("MANAGER"))).StatusCode);
        var access = await admin.GetFromJsonAsync<InternalAccountAccessResponse>($"{Path}/{account.Id}/effective-access");
        Assert.Equal("MANAGER", access!.Role);
        Assert.Contains(access.Permissions, x => x.Code == "MANAGE_OPERATIONS");
        using var loginClient = database.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await loginClient.GetFromJsonAsync<CsrfResponse>("api/v1/auth/csrf");
        loginClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        using var login = await loginClient.PostAsJsonAsync("api/v1/auth/login", new LoginRequest(request.Username, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal("MANAGER", session.User.Role);
        loginClient.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Path}/{account.Id}/status", new ChangeStatusRequest("LOCKED"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await loginClient.GetAsync("api/v1/auth/me")).StatusCode);
        using var lockedLogin = await loginClient.PostAsJsonAsync("api/v1/auth/login", new LoginRequest(request.Username, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, lockedLogin.StatusCode);
        foreach (var status in new[]{"DISABLED", "ACTIVE"})
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Path}/{account.Id}/status", new ChangeStatusRequest(status))).StatusCode);
        loginClient.DefaultRequestHeaders.Authorization = null;
        using var relogin = await loginClient.PostAsJsonAsync("api/v1/auth/login", new LoginRequest(request.Username, Password));
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        var activity = await admin.GetFromJsonAsync<PagedAdministrationActivitiesResponse>("api/v1/administration/activity?pageSize=100");
        Assert.Contains(activity!.Items, x => x.TargetAccountId == account.Id && x.Action == "internal_account_created");
        Assert.Contains(activity.Items, x => x.TargetAccountId == account.Id && x.Action == "internal_account_role_changed");
        Assert.Equal(3, activity.Items.Count(x => x.TargetAccountId == account.Id && x.Action == "internal_account_status_changed"));
    }
}
