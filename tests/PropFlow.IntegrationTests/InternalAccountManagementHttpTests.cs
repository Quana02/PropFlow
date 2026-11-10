using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

    private static async Task<InternalAccountResponse> CreateAsync(HttpClient admin, CreateInternalAccountRequest request)
    {
        using var response = await admin.PostAsJsonAsync(Path, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InternalAccountResponse>())!;
    }

    private async Task<(HttpClient Client, SessionResponse Session)> LoginAsync(string username)
    {
        var client = database.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("api/v1/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        using var login = await client.PostAsJsonAsync("api/v1/auth/login", new LoginRequest(username, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        return (client, session);
    }

    [Theory]
    [InlineData("MANAGER", true, false)]
    [InlineData("STAFF", false, true)]
    [InlineData("ACCOUNTANT", true, true)]
    public async Task Admin_updates_only_allowed_profile_fields_and_preserves_sessions(string role, bool changeName, bool changePhone)
    {
        using var admin = await ClientAsync("ADMIN");
        var actor = (await admin.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!;
        var account = await CreateAsync(admin, Request(role));
        var (client, session) = await LoginAsync(account.Username);
        using var target = client;
        var path = $"{Path}/{account.Id}";
        var before = (await admin.GetFromJsonAsync<InternalAccountProfileResponse>(path))!;
        Assert.Equal(role, before.Role);
        Assert.Equal(account.DisplayName, before.DisplayName);
        Assert.Equal(session.User.EmailVerified, before.EmailVerified);

        var name = changeName ? "Tên do Admin cập nhật" : before.DisplayName;
        var phone = changePhone ? (changeName ? "0901234501" : "0901234502") : null;
        using var updated = await admin.PutAsJsonAsync(path + "/profile", new UpdateInternalAccountProfileRequest($" {name} ", phone is null ? null : $" {phone} "));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var expected = before with { DisplayName = name, PhoneNumber = phone };
        Assert.Equal(expected, await updated.Content.ReadFromJsonAsync<InternalAccountProfileResponse>());
        Assert.Equal(expected, await admin.GetFromJsonAsync<InternalAccountProfileResponse>(path));
        var listed = await admin.GetFromJsonAsync<PagedInternalAccountsResponse>($"{Path}?role={role}&status=ACTIVE&search={account.Username}&pageSize=1");
        Assert.Equal(name, Assert.Single(listed!.Items).DisplayName);

        var current = (await target.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!;
        Assert.Equal(name, current.DisplayName);
        Assert.Equal(phone, current.PhoneNumber);
        Assert.Equal(session.User.Permissions.Order(), current.Permissions.Order());
        target.DefaultRequestHeaders.Authorization = null;
        using var refresh = await target.PostAsync("api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(name, (await refresh.Content.ReadFromJsonAsync<SessionResponse>())!.User.DisplayName);

        var activity = await admin.GetFromJsonAsync<PagedAdministrationActivitiesResponse>(
            "api/v1/administration/system-activity?action=internal_account_profile_updated");
        var audit = Assert.Single(activity!.Items, item => item.TargetAccountId == account.Id);
        Assert.Equal(actor.Id, audit.ActorUserId);
        Assert.Equal("SUCCESS", audit.Outcome);
        Assert.Equal(200, audit.HttpStatus);
        Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));
        var fields = new List<string>();
        if (changeName) fields.Add("DisplayName");
        if (changePhone) fields.Add("PhoneNumber");
        Assert.Equal(fields, audit.Changes!.Select(change => change.Field));
        Assert.All(audit.Changes!, change =>
        {
            Assert.Equal("[Đã ẩn nội dung]", change.Before);
            Assert.Equal("[Đã ẩn nội dung]", change.After);
        });
    }

    [Theory]
    [InlineData("MANAGER")]
    [InlineData("STAFF")]
    [InlineData("ACCOUNTANT")]
    [InlineData("RESIDENT")]
    public async Task Other_roles_cannot_read_or_edit_managed_profile(string role)
    {
        using var admin = await ClientAsync("ADMIN");
        var account = await CreateAsync(admin, Request());
        using var actor = await ClientAsync(role);
        using var read = await actor.GetAsync($"{Path}/{account.Id}");
        using var write = await actor.PutAsJsonAsync($"{Path}/{account.Id}/profile", new UpdateInternalAccountProfileRequest("Forbidden", null));
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Profile_rejects_residents_admin_and_missing_targets()
    {
        using var admin = await ClientAsync("ADMIN");
        using var resident = await ClientAsync("RESIDENT");
        var adminId = (await admin.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Id;
        var residentId = (await resident.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Id;
        foreach (var id in new[] { adminId, residentId, Guid.NewGuid() })
        {
            using var read = await admin.GetAsync($"{Path}/{id}");
            using var write = await admin.PutAsJsonAsync($"{Path}/{id}/profile", new UpdateInternalAccountProfileRequest("Forbidden target", null));
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        }
    }

    [Fact]
    public async Task Invalid_profile_and_duplicate_phone_leave_existing_data_intact_and_record_failure()
    {
        using var admin = await ClientAsync("ADMIN");
        var actor = (await admin.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!;
        const string phone = "0912456738";
        await CreateAsync(admin, Request() with { PhoneNumber = phone });
        var account = await CreateAsync(admin, Request());
        var path = $"{Path}/{account.Id}";
        var before = await admin.GetFromJsonAsync<InternalAccountProfileResponse>(path);

        foreach (var field in new[] { "username", "email", "role", "status", "password", "otp", "refreshToken", "id" })
        {
            using var extra = await admin.PutAsJsonAsync(path + "/profile",
                new Dictionary<string, string?> { ["displayName"] = "Attempt", ["phoneNumber"] = null, [field] = "injected" });
            Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        }
        using var missing = await admin.PutAsJsonAsync(path + "/profile", new { displayName = "Attempt" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var invalid = await admin.PutAsJsonAsync(path + "/profile", new UpdateInternalAccountProfileRequest(" ", new string('1', 21)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var validation = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Equal(2, validation.RootElement.GetProperty("errors").EnumerateObject().Count());

        using var duplicate = await admin.PutAsJsonAsync(path + "/profile", new UpdateInternalAccountProfileRequest("Must roll back", $" {phone} "));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("phone_conflict", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await admin.GetFromJsonAsync<InternalAccountProfileResponse>(path));

        var activity = await admin.GetFromJsonAsync<PagedAdministrationActivitiesResponse>(
            "api/v1/administration/system-activity?action=internal_account_profile_updated&outcome=FAILED");
        var audit = Assert.Single(activity!.Items, item => item.TargetAccountId == account.Id && item.HttpStatus == 409);
        Assert.Equal(actor.Id, audit.ActorUserId);
        Assert.Equal(problem.RootElement.GetProperty("traceId").GetString(), audit.CorrelationId);
        Assert.Empty(audit.Changes!);
    }

    [Fact]
    public async Task Concurrent_admin_and_self_edits_save_a_complete_profile_and_keep_access()
    {
        using var admin = await ClientAsync("ADMIN");
        var account = await CreateAsync(admin, Request("MANAGER"));
        var (client, session) = await LoginAsync(account.Username);
        using var target = client;
        var path = $"{Path}/{account.Id}";
        var results = await Task.WhenAll(
            admin.PutAsJsonAsync(path + "/profile", new UpdateInternalAccountProfileRequest("Admin edit", "0912345678")),
            target.PutAsJsonAsync("api/v1/auth/me", new UpdateProfileRequest("Self edit", "0987654321")));
        foreach (var response in results)
        {
            using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var actual = (await admin.GetFromJsonAsync<InternalAccountProfileResponse>(path))!;
        Assert.Contains((actual.DisplayName, actual.PhoneNumber),
            new (string, string?)[] { ("Admin edit", "0912345678"), ("Self edit", "0987654321") });
        Assert.Equal(session.User.Email, actual.Email);
        Assert.Equal(session.User.Status, actual.Status);
        Assert.Equal(session.User.Role, actual.Role);
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
