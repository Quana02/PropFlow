using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Domain.ResidentApartments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

// Real HTTP handlers and PostgreSQL. Only outbound email is captured, exclusively in this test host.
public sealed class AuthTestMail : IAuthEmail
{
    public ConcurrentDictionary<string, string> Codes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Task SendCodeAsync(string email, string code, bool recovery, int expiryMinutes, CancellationToken ct)
    { Codes[email] = code; return Task.CompletedTask; }
}
public sealed class AuthTestClock : TimeProvider
{
    public TimeSpan Offset { get; set; }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(Offset);
}
public sealed class AuthFlowFactory(string connection) : PropFlowApiFactory
{
    public AuthTestMail Mail { get; } = new();
    public AuthTestClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PropFlowDatabase"] = connection,
            ["Authentication:RateLimits:auth-login:PermitLimit"] = "1000",
            ["Authentication:RateLimits:auth-entry:PermitLimit"] = "1000"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthEmail>(); services.AddSingleton<IAuthEmail>(Mail);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

public sealed class AuthDatabaseFixture : IAsyncLifetime
{
    private string databaseName = "";
    private string adminConnection = "";
    public AuthFlowFactory Factory { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "PropFlow.sln"))) root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("Không tìm thấy PropFlow.sln từ thư mục kiểm thử.");
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root.FullName, "src/PropFlow.Api/appsettings.Testing.json")).Build();
        var configured = new NpgsqlConnectionStringBuilder(config.GetConnectionString("PropFlowDatabase"));
        if (configured.Database != "propflow_test") throw new InvalidOperationException("Auth tests require the configured propflow_test connection as the isolated test server source.");
        databaseName = "propflow_fe01_test_" + Guid.NewGuid().ToString("N");
        configured.Database = "postgres";
        adminConnection = configured.ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await command.ExecuteNonQueryAsync();
        }
        configured.Database = databaseName;
        Factory = new AuthFlowFactory(configured.ConnectionString);
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        foreach (var context in new DbContext[] { services.GetRequiredService<AuthenticationDbContext>(), services.GetRequiredService<PropertyAssetsDbContext>(), services.GetRequiredService<ApartmentsDbContext>(), services.GetRequiredService<ResidentsDbContext>(), services.GetRequiredService<AdministrationDbContext>() })
            Assert.Equal(databaseName, context.Database.GetDbConnection().Database);
        // Initial schemas must exist before the cross-module integrity migrations run.
        await services.GetRequiredService<AuthenticationDbContext>().GetService<IMigrator>().MigrateAsync("20260914101250_CorrectAuthenticationEmailOtpVerification");
        await services.GetRequiredService<PropertyAssetsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ApartmentsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ResidentsDbContext>().GetService<IMigrator>().MigrateAsync("20260914063807_InitialResidents");
        await services.GetRequiredService<AdministrationDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<AuthenticationDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ResidentsDbContext>().Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        if (Factory != null) await Factory.DisposeAsync();
        if (!databaseName.StartsWith("propflow_fe01_test_", StringComparison.Ordinal) || databaseName.Length != 51) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
    public async Task SeedResidentAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var now = DateTimeOffset.UtcNow;
        var building = new Building(Guid.NewGuid().ToString("N")[..12], "Tòa nhà kiểm thử", "Địa chỉ kiểm thử", now);
        var assets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>(); assets.Buildings.Add(building); await assets.SaveChangesAsync();
        var apartment = new ApartmentUnit(building.Id, "A101", 1, now);
        var apartments = scope.ServiceProvider.GetRequiredService<ApartmentsDbContext>(); apartments.ApartmentUnits.Add(apartment); await apartments.SaveChangesAsync();
        var resident = new Resident(Guid.NewGuid().ToString("N")[..20], "Cư dân kiểm thử", now, email: email);
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>(); residents.Residents.Add(resident);
        residents.ResidentApartments.Add(new ResidentApartment(resident.Id, apartment.Id, "OWNER", DateOnly.FromDateTime(now.AddDays(-2).DateTime), now));
        await residents.SaveChangesAsync();
    }
}

[Trait("Feature", "FE-01")]
public sealed class AuthenticationFlowTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    private const string Password = "A secure passphrase 123!";
    private HttpClient Client() => database.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, bool csrf = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/" + path) { Content = JsonContent.Create(body) };
        if (csrf) { var token = await client.GetFromJsonAsync<CsrfResponse>("api/v1/auth/csrf"); request.Headers.Add("X-CSRF-TOKEN", token!.Token); }
        return await client.SendAsync(request);
    }
    private async Task<(string Username, string Email)> ActivatedAccount(HttpClient client)
    {
        var username = "resident_" + Guid.NewGuid().ToString("N")[..12];
        var email = username + "@example.invalid";
        await database.SeedResidentAsync(email);
        var registration = await Post(client, "register", new RegisterRequest(username, "Cư dân kiểm thử", email, Password, null));
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var challenge = await registration.Content.ReadFromJsonAsync<ChallengeResponse>();
        Assert.NotEqual(Guid.Empty, challenge!.ChallengeId);
        var activate = await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, database.Factory.Mail.Codes[email]));
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        using (var scope = database.Factory.Services.CreateScope())
        {
            var userId = await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().UserAccounts.Where(x => x.Username == username).Select(x => x.Id).SingleAsync();
            var history = await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().UserAccessHistories.SingleAsync(x => x.TargetUserId == userId);
            Assert.Equal(PropFlow.Modules.Administration.Domain.UserAccessHistories.AccessActionType.ROLE_CHANGED, history.Action);
            Assert.NotNull(history.NewRoleId);
        }
        return (username, email);
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.2/FE-01.3/FE-01.6/FE-01.7")]
    public async Task Resident_can_activate_login_rotate_restore_update_profile_and_logout()
    {
        using var client = Client();
        var account = await ActivatedAccount(client);
        var login = await Post(client, "login", new LoginRequest(account.Username, Password), true);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookies = string.Join(";", login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookies, StringComparison.OrdinalIgnoreCase); Assert.Contains("secure", cookies, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookies, StringComparison.OrdinalIgnoreCase);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal("RESIDENT", session.User.Role);
        Assert.DoesNotContain("refreshToken", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        Assert.Equal(session.User.Id, (await client.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Id);
        var update = await client.PutAsJsonAsync("api/v1/auth/me", new UpdateProfileRequest("Tên mới", null));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        // Browser reload: access token is gone; cookie alone must bootstrap a new session.
        client.DefaultRequestHeaders.Authorization = null;
        var refresh = await Post(client, "refresh", new { }, true);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.NotEqual(session.AccessToken, (await refresh.Content.ReadFromJsonAsync<SessionResponse>())!.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "logout", new { }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.4/FE-01.5")]
    public async Task Recovery_proof_is_single_use_and_password_changes_revoke_sessions()
    {
        using var client = Client(); var account = await ActivatedAccount(client);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "login", new LoginRequest(account.Username, Password), true)).StatusCode);
        var forgot = await Post(client, "password/forgot", new ForgotPasswordRequest(account.Email));
        var challenge = (await forgot.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var verify = await Post(client, "password/verify", new VerifyChallengeRequest(challenge.ChallengeId, database.Factory.Mail.Codes[account.Email]));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var proof = (await verify.Content.ReadFromJsonAsync<ResetProofResponse>())!;
        var newPassword = "New secure passphrase 456!";
        var resetRequest = new ResetPasswordRequest(proof.ChallengeId, proof.Proof, newPassword);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "password/reset", resetRequest, true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "password/reset", resetRequest, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(account.Username, Password), true)).StatusCode);
        var loggedIn = await Post(client, "login", new LoginRequest(account.Username, newPassword), true);
        var session = (await loggedIn.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "password/change", new ChangePasswordRequest(newPassword, Password), true)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.1/FE-01.3")]
    public async Task Missing_eligibility_cannot_activate_and_wrong_OTP_attempts_are_persisted()
    {
        using var client = Client();
        var username = "pending_" + Guid.NewGuid().ToString("N")[..12]; var email = username + "@example.invalid";
        var pending = await Post(client, "register", new RegisterRequest(username, "Chờ xác minh", email, Password, null));
        Assert.Equal(Guid.Empty, (await pending.Content.ReadFromJsonAsync<ChallengeResponse>())!.ChallengeId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(username, Password), true)).StatusCode);
        await database.SeedResidentAsync(email);
        var resumed = await Post(client, "registration/resend", new ResumeRegistrationRequest(username, Password));
        var challenge = (await resumed.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var correct = database.Factory.Mail.Codes[email]; var wrong = correct == "000000" ? "111111" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, wrong))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "registration/verify", new VerifyChallengeRequest(challenge.ChallengeId, correct))).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.2/FE-01.4/FE-01.7")]
    public async Task Cookie_operations_require_csrf_and_unknown_recovery_is_neutral()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "refresh", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "logout", new { })).StatusCode);
        var response = await Post(client, "password/forgot", new ForgotPasswordRequest("absent@example.invalid"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        Assert.NotEqual(Guid.Empty, challenge.ChallengeId);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "password/verify", new VerifyChallengeRequest(challenge.ChallengeId, "123456"))).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.2/FE-01.7")]
    public async Task Rotation_rejects_replay_and_logout_preserves_other_devices()
    {
        using var client = Client(); var account = await ActivatedAccount(client);
        var login = await Post(client, "login", new LoginRequest(account.Username, Password), true);
        var oldCookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Secure-PropFlow.Refresh=")).Split(';')[0];
        using var device = Client();
        Assert.Equal(HttpStatusCode.OK, (await Post(device, "login", new LoginRequest(account.Username, Password), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "refresh", new { }, true)).StatusCode);
        using var replay = database.Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), HandleCookies = false });
        var csrf = await replay.GetAsync("api/v1/auth/csrf");
        var token = (await csrf.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/refresh");
        request.Headers.Add("Cookie", oldCookie + "; " + csrf.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Secure-PropFlow.Csrf=")).Split(';')[0]);
        request.Headers.Add("X-CSRF-TOKEN", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "logout", new { }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(device, "refresh", new { }, true)).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.3")]
    public async Task Resend_is_throttled_cancels_old_challenge_and_expiry_is_enforced()
    {
        using var client = Client();
        var username = "expiry_" + Guid.NewGuid().ToString("N")[..12]; var email = username + "@example.invalid";
        await database.SeedResidentAsync(email);
        var registered = await Post(client, "register", new RegisterRequest(username, "Kiểm thử hết hạn", email, Password, null));
        var first = (await registered.Content.ReadFromJsonAsync<ChallengeResponse>())!;
        var firstCode = database.Factory.Mail.Codes[email];
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(client, "registration/resend", new ResumeRegistrationRequest(username, Password))).StatusCode);
        try
        {
            database.Factory.Clock.Offset = TimeSpan.FromSeconds(61);
            var resent = await Post(client, "registration/resend", new ResumeRegistrationRequest(username, Password));
            Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
            var second = (await resent.Content.ReadFromJsonAsync<ChallengeResponse>())!;
            Assert.NotEqual(first.ChallengeId, second.ChallengeId);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "registration/verify", new VerifyChallengeRequest(first.ChallengeId, firstCode))).StatusCode);
            database.Factory.Clock.Offset = TimeSpan.FromMinutes(7);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "registration/verify", new VerifyChallengeRequest(second.ChallengeId, database.Factory.Mail.Codes[email]))).StatusCode);
        }
        finally { database.Factory.Clock.Offset = TimeSpan.Zero; }
    }

    [Fact, Trait("UseCase", "FE-01.2/FE-01.6")]
    public async Task Lockout_is_enforced_and_profile_payload_cannot_escalate_role()
    {
        using var client = Client(); var account = await ActivatedAccount(client);
        var login = await Post(client, "login", new LoginRequest(account.Username, Password), true);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        var update = await client.PutAsJsonAsync("api/v1/auth/me", new { displayName = "Tên hợp lệ", role = "ADMIN", status = "ACTIVE", email = "other@example.invalid", residentId = Guid.NewGuid(), apartmentUnitId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var profile = (await update.Content.ReadFromJsonAsync<AccountResponse>())!;
        Assert.Equal("RESIDENT", profile.Role); Assert.Equal(account.Email.ToUpperInvariant(), profile.Email);
        client.DefaultRequestHeaders.Authorization = null;
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(account.Username, "incorrect password"), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "login", new LoginRequest(account.Username, Password), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { }, true)).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.2/FE-01.6")]
    public async Task Expired_bearer_is_401_but_removed_account_access_is_403()
    {
        using var client = Client(); var account = await ActivatedAccount(client);
        var login = await Post(client, "login", new LoginRequest(account.Username, Password), true);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        using (var scope = database.Factory.Services.CreateScope())
        {
            var expired = scope.ServiceProvider.GetRequiredService<IAuthSecrets>().Issue(session.User, DateTimeOffset.UtcNow.AddHours(-1));
            client.DefaultRequestHeaders.Authorization = new("Bearer", expired.AccessToken);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/auth/me")).StatusCode);
        var refreshed = await Post(client, "refresh", new { }, true);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await refreshed.Content.ReadFromJsonAsync<SessionResponse>())!.AccessToken);
        using (var scope = database.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AdministrationDbContext>().UserRoleAssignments.Where(x => x.UserId == session.User.Id).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/v1/auth/me")).StatusCode);
    }

    [Fact, Trait("UseCase", "FE-01.2")]
    public async Task Credentialed_CORS_allows_only_configured_frontend_origin()
    {
        using var client = Client();
        using var accepted = new HttpRequestMessage(HttpMethod.Options, "api/v1/auth/refresh");
        accepted.Headers.Add("Origin", "https://localhost:7201");
        accepted.Headers.Add("Access-Control-Request-Method", "POST");
        accepted.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");
        using var acceptedResponse = await client.SendAsync(accepted);
        Assert.Equal(HttpStatusCode.NoContent, acceptedResponse.StatusCode);
        Assert.Equal("https://localhost:7201", acceptedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", acceptedResponse.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        using var rejected = new HttpRequestMessage(HttpMethod.Options, "api/v1/auth/refresh");
        rejected.Headers.Add("Origin", "https://untrusted.example.invalid");
        rejected.Headers.Add("Access-Control-Request-Method", "POST");
        using var rejectedResponse = await client.SendAsync(rejected);
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
