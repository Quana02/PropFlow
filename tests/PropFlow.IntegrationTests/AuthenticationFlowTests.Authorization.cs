using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

internal sealed class AuthTestAvatarStorage : IAccountAvatarStorage
{
    public ConcurrentDictionary<string, (Guid Owner, byte[] Content)> Files { get; } = new();
    public ConcurrentQueue<Guid> Uploads { get; } = new();
    public ConcurrentQueue<string> Deletes { get; } = new();
    public async Task<string> UploadAsync(Guid owner, Stream content, string contentType, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        var key = Guid.NewGuid().ToString("N");
        Files[key] = (owner, copy.ToArray());
        Uploads.Enqueue(owner);
        return key;
    }
    public Task<byte[]> ReadAsync(string storageKey, CancellationToken ct) => Task.FromResult(Files[storageKey].Content);
    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        Deletes.Enqueue(storageKey);
        Files.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }
}

public sealed partial class AuthenticationFlowTests
{
    private record RefreshSnapshot(Guid Id, Guid? SessionId, DateTimeOffset? RevokedAt, string? RevokedReason, Guid? ReplacedByTokenId);
    private record AccountSecurity(string PasswordHash, string? Email, AccountStatus Status, bool EmailVerified, RefreshSnapshot[] Refresh);

    private static async Task<AccountSecurity> ReadAccountSecurity(AuthenticationDbContext auth, Guid id)
    {
        var account = await auth.UserAccounts.AsNoTracking().SingleAsync(row => row.Id == id);
        var sessions = await auth.RefreshTokens.Where(row => row.UserId == id).OrderBy(row => row.Id)
            .Select(row => new RefreshSnapshot(row.Id, row.SessionId, row.RevokedAt, row.RevokedReason, row.ReplacedByTokenId))
            .ToArrayAsync();
        return new(account.PasswordHash, account.Email, account.Status, account.EmailVerified, sessions);
    }

    private static void AssertSameSecurity(AccountSecurity expected, AccountSecurity actual)
    {
        Assert.True(expected.PasswordHash == actual.PasswordHash, "Credential hash changed.");
        Assert.Equal(expected.Email, actual.Email);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.EmailVerified, actual.EmailVerified);
        Assert.Equal(expected.Refresh, actual.Refresh);
    }

    private static async Task<SessionResponse> SignIn(HttpClient client, string username, string password = Password)
    {
        using var result = await Post(client, "login", new LoginRequest(username, password), true);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<SessionResponse>())!;
    }

    private async Task<HttpStatusCode> BearerStatus(string accessToken)
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        using var result = await client.GetAsync("api/v1/auth/me");
        return result.StatusCode;
    }

    private static async Task<ChallengeResponse> RecoveryChallenge(HttpClient client, string email)
    {
        using var result = await Post(client, "password/forgot", new ForgotPasswordRequest(email));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<ChallengeResponse>())!;
    }

    private static async Task<ResetProofResponse> VerifyRecovery(HttpClient client, Guid id, string otp)
    {
        using var result = await Post(client, "password/verify", new VerifyChallengeRequest(id, otp));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var proof = (await result.Content.ReadFromJsonAsync<ResetProofResponse>())!;
        Assert.Equal(id, proof.ChallengeId);
        Assert.True(!string.IsNullOrWhiteSpace(proof.Proof));
        return proof;
    }

    [Fact]
    public async Task Registration_verification_binds_the_challenge_and_links_only_once()
    {
        using var client = Client();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var first = await Database.SeedRegistrationResidentAsync($"verify-a-{suffix}@example.invalid", currentOwnership: true);
        var second = await Database.SeedRegistrationResidentAsync($"verify-b-{suffix}@example.invalid", activeResidency: true);
        var firstName = "verify_a_" + suffix;
        var secondName = "verify_b_" + suffix;
        using var beginA = await Post(client, "register", await RegistrationRequestAsync(firstName, "Kiểm thử chủ sở hữu", first.Email));
        using var beginB = await Post(client, "register", await RegistrationRequestAsync(secondName, "Kiểm thử chủ hộ thuê", second.Email));
        Assert.Equal(HttpStatusCode.OK, beginA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, beginB.StatusCode);
        var challengeA = (await beginA.Content.ReadFromJsonAsync<ChallengeResponse>())!.ChallengeId;
        var challengeB = (await beginB.Content.ReadFromJsonAsync<ChallengeResponse>())!.ChallengeId;
        Assert.NotEqual(Guid.Empty, challengeA);
        Assert.NotEqual(Guid.Empty, challengeB);
        using var scope = Database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var otpA = Database.Factory.Mail.Codes[first.Email];
        var otpB = Database.Factory.Mail.Codes[second.Email];
        var deniedCode = otpA != otpB ? otpB : otpA == "111111" ? "222222" : "111111";
        using var denied = await Post(client, "registration/verify", new VerifyChallengeRequest(challengeA, deniedCode));
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal(1, await auth.ResidentVerifications.Where(row => row.Id == challengeA).Select(row => row.AttemptCount).SingleAsync());
        Assert.Equal(0, await auth.ResidentVerifications.Where(row => row.Id == challengeB).Select(row => row.AttemptCount).SingleAsync());
        Assert.False(await auth.UserAccounts.AnyAsync(row => row.Username == firstName || row.Username == secondName));
        Assert.False(await residents.Residents.AnyAsync(row => (row.Id == first.ResidentId || row.Id == second.ResidentId) && row.UserId != null));
        var response = new VerifyChallengeRequest(challengeA, otpA);
        using var activated = await Post(client, "registration/verify", response);
        Assert.Equal(HttpStatusCode.NoContent, activated.StatusCode);
        using var replay = await Post(client, "registration/verify", response);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var account = await auth.UserAccounts.AsNoTracking().SingleAsync(row => row.Username == firstName);
        Assert.Equal(AccountStatus.ACTIVE, account.Status);
        Assert.True(account.EmailVerified);
        Assert.Equal(account.Id, await residents.Residents.Where(row => row.Id == first.ResidentId).Select(row => row.UserId).SingleAsync());
        Assert.Null(await residents.Residents.Where(row => row.Id == second.ResidentId).Select(row => row.UserId).SingleAsync());
        var verified = await auth.ResidentVerifications.AsNoTracking().SingleAsync(row => row.Id == challengeA);
        Assert.Equal(account.Id, verified.UserId);
        Assert.NotNull(verified.VerifiedAt);
        Assert.False(await auth.RefreshTokens.AnyAsync(row => row.UserId == account.Id));
        using var tenantActivated = await Post(client, "registration/verify", new VerifyChallengeRequest(challengeB, otpB));
        Assert.Equal(HttpStatusCode.NoContent, tenantActivated.StatusCode);
        var tenant = await SignIn(client, secondName);
        Assert.Equal("RESIDENT", tenant.User.Role);
        Assert.Equal(tenant.User.Id, await residents.Residents.Where(row => row.Id == second.ResidentId).Select(row => row.UserId).SingleAsync());
    }

    [Fact]
    public async Task Csrf_cookie_pair_rejects_missing_and_foreign_tokens_without_starting_a_session()
    {
        using var caller = Client();
        using var foreign = Client();
        var account = await ActivatedAccount(caller);
        using var scope = Database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var id = await auth.UserAccounts.Where(row => row.Username == account.Username).Select(row => row.Id).SingleAsync();
        var before = await ReadAccountSecurity(auth, id);
        using var csrf = await caller.GetAsync("api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var cookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie").Where(value => value.StartsWith("__Secure-PropFlow.Csrf=")));
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", cookie, StringComparison.OrdinalIgnoreCase);
        var issued = (await csrf.Content.ReadFromJsonAsync<CsrfResponse>())!;
        var other = (await foreign.GetFromJsonAsync<CsrfResponse>("api/v1/auth/csrf"))!;
        Assert.True(!string.IsNullOrWhiteSpace(issued.Token) && issued.Token != other.Token);
        foreach (var token in new[] { null, other.Token })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest(account.Username, Password))
            };
            if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
            using var response = await caller.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertNoRefreshCookie(response);
        }
        AssertSameSecurity(before, await ReadAccountSecurity(auth, id));
        Assert.Equal(id, (await SignIn(caller, account.Username)).User.Id);
        Assert.Single((await ReadAccountSecurity(auth, id)).Refresh);
    }

    private static void AssertNoRefreshCookie(HttpResponseMessage response)
    {
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(value => value.StartsWith("__Secure-PropFlow.Refresh=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Login_only_issues_a_session_for_valid_credentials_and_current_access()
    {
        using var caller = Client();
        var active = await ActivatedAccount(caller);
        var disabled = await ActivatedAccount(caller);
        var pendingName = "pending_" + Guid.NewGuid().ToString("N")[..12];
        using var scope = Database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var target = await auth.UserAccounts.SingleAsync(row => row.Username == active.Username);
        var inactive = await auth.UserAccounts.SingleAsync(row => row.Username == disabled.Username);
        var now = Database.Factory.Clock.GetUtcNow();
        inactive.Disable(target.Id, now);
        var pending = new UserAccount(pendingName, "pending", "Kiểm thử chờ kích hoạt", now, pendingName + "@example.invalid");
        pending.ChangePasswordHash(scope.ServiceProvider.GetRequiredService<IAuthSecrets>().HashPassword(pending, Password), null, now);
        auth.UserAccounts.Add(pending);
        await auth.SaveChangesAsync();
        var before = await ReadAccountSecurity(auth, target.Id);
        var disabledBefore = await ReadAccountSecurity(auth, inactive.Id);
        var pendingBefore = await ReadAccountSecurity(auth, pending.Id);
        var failures = new List<(string? Code, string? Title)>();
        foreach (var credentials in new[] { new LoginRequest(active.Username, "incorrect password"),
            new LoginRequest("unknown_" + Guid.NewGuid().ToString("N")[..12], Password),
            new LoginRequest(disabled.Username, Password), new LoginRequest(pendingName, Password) })
        {
            using var denied = await Post(caller, "login", credentials, true);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            AssertNoRefreshCookie(denied);
            using var error = System.Text.Json.JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            Assert.False(error.RootElement.TryGetProperty("accessToken", out _));
            Assert.False(error.RootElement.TryGetProperty("user", out _));
            failures.Add((error.RootElement.GetProperty("code").GetString(), error.RootElement.GetProperty("title").GetString()));
        }
        Assert.Single(failures.Distinct());
        AssertSameSecurity(before, await ReadAccountSecurity(auth, target.Id));
        AssertSameSecurity(disabledBefore, await ReadAccountSecurity(auth, inactive.Id));
        AssertSameSecurity(pendingBefore, await ReadAccountSecurity(auth, pending.Id));
        Assert.Equal(1, await auth.UserAccounts.Where(row => row.Id == target.Id).Select(row => row.FailedLoginCount).SingleAsync());
        using var login = await Post(caller, "login", new { active.Username, password = Password, userId = inactive.Id, role = "ADMIN", permissions = new[] { "MANAGE_USERS" } }, true);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Secure-PropFlow.Refresh="));
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        var row = Assert.Single((await ReadAccountSecurity(auth, target.Id)).Refresh);
        Assert.Equal(target.Id, session.User.Id);
        Assert.Equal(target.Id.ToString(), jwt.Subject);
        Assert.Equal("RESIDENT", session.User.Role);
        Assert.Equal(new[] { "USE_RESIDENT_SERVICES" }, session.User.Permissions);
        Assert.Equal(session.User.Permissions, jwt.Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value));
        Assert.Equal(row.SessionId, Guid.Parse(jwt.Claims.Single(claim => claim.Type == "sid").Value));
        Assert.Null(row.RevokedAt);
        Assert.Equal(HttpStatusCode.OK, await BearerStatus(session.AccessToken));

        using var limitedHost = Database.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:RateLimits:auth-login:PermitLimit"] = "2" })));
        using var limited = limitedHost.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var firstLimitedAttempt = await Post(limited, "login", new LoginRequest(active.Username, "incorrect password"), true);
        Assert.Equal(HttpStatusCode.Unauthorized, firstLimitedAttempt.StatusCode);
        Assert.Equal(target.Id, (await SignIn(limited, active.Username)).User.Id);
        var beforeThrottle = await ReadAccountSecurity(auth, target.Id);
        using var limitedAttempt = await Post(limited, "login", new LoginRequest(active.Username, Password), true);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedAttempt.StatusCode);
        AssertNoRefreshCookie(limitedAttempt);
        AssertSameSecurity(beforeThrottle, await ReadAccountSecurity(auth, target.Id));
    }

    [Fact]
    public async Task Recovery_start_creates_a_challenge_only_for_an_eligible_account()
    {
        using var client = Client();
        var eligible = await ActivatedAccount(client);
        var disabled = await ActivatedAccount(client);
        using var scope = Database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var active = await auth.UserAccounts.SingleAsync(row => row.Username == eligible.Username);
        var inactive = await auth.UserAccounts.SingleAsync(row => row.Username == disabled.Username);
        inactive.Disable(active.Id, Database.Factory.Clock.GetUtcNow());
        await auth.SaveChangesAsync();
        var before = await auth.PasswordResetTokens.CountAsync();
        var sentBefore = Database.Factory.Mail.RecoveryDeliveries;
        var unknownEmail = Guid.NewGuid().ToString("N") + "@example.invalid";
        var unknown = await RecoveryChallenge(client, unknownEmail);
        var denied = await RecoveryChallenge(client, disabled.Email);
        var started = await RecoveryChallenge(client, eligible.Email);
        var throttled = await RecoveryChallenge(client, eligible.Email);
        Assert.Equal(started.Message, unknown.Message);
        Assert.Equal(started.Message, denied.Message);
        Assert.Equal(started.Message, throttled.Message);
        Assert.Equal(before + 1, await auth.PasswordResetTokens.CountAsync());
        var challenge = await auth.PasswordResetTokens.SingleAsync(row => row.Id == started.ChallengeId);
        Assert.Equal(active.Id, challenge.UserId);
        Assert.Null(challenge.UsedAt);
        Assert.Equal(0, challenge.AttemptCount);
        Assert.Equal(sentBefore + 1, Database.Factory.Mail.RecoveryDeliveries);
        Assert.False(await auth.PasswordResetTokens.AnyAsync(row =>
            row.Id == unknown.ChallengeId || row.Id == denied.ChallengeId || row.Id == throttled.ChallengeId));
    }

    [Fact]
    public async Task Password_change_requires_a_current_session_and_only_changes_the_caller()
    {
        using var caller = Client();
        using var revokedDevice = Client();
        using var otherDevice = Client();
        using var unrelated = Client();
        using var anonymous = Client();
        var account = await ActivatedAccount(caller);
        var otherAccount = await ActivatedAccount(unrelated);
        var current = await SignIn(caller, account.Username);
        var revoked = await SignIn(revokedDevice, account.Username);
        var independent = await SignIn(otherDevice, account.Username);
        var other = await SignIn(unrelated, otherAccount.Username);
        using var logout = await Post(revokedDevice, "logout", new { }, true);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        caller.DefaultRequestHeaders.Authorization = new("Bearer", current.AccessToken);
        revokedDevice.DefaultRequestHeaders.Authorization = new("Bearer", revoked.AccessToken);
        const string newPassword = "B1 changed password 456!";
        var request = new { currentPassword = Password, newPassword, userId = other.User.Id, role = "ADMIN" };
        using var scope = Database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var before = await ReadAccountSecurity(auth, current.User.Id);
        var otherBefore = await ReadAccountSecurity(auth, other.User.Id);
        using var noBearer = await Post(anonymous, "password/change", request, true);
        using var staleBearer = await Post(revokedDevice, "password/change", request, true);
        using var wrong = await Post(caller, "password/change", request with { currentPassword = "incorrect password" }, true);
        using var weak = await Post(caller, "password/change", request with { newPassword = "short" }, true);
        Assert.Equal(HttpStatusCode.Unauthorized, noBearer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, staleBearer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        AssertSameSecurity(before, await ReadAccountSecurity(auth, current.User.Id));
        AssertSameSecurity(otherBefore, await ReadAccountSecurity(auth, other.User.Id));
        using var changed = await Post(caller, "password/change", request, true);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        var after = await ReadAccountSecurity(auth, current.User.Id);
        Assert.True(before.PasswordHash != after.PasswordHash);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Refresh.Length, after.Refresh.Length);
        Assert.All(after.Refresh, row => Assert.NotNull(row.RevokedAt));
        AssertSameSecurity(otherBefore, await ReadAccountSecurity(auth, other.User.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(current.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(independent.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await BearerStatus(other.AccessToken));
        using var oldRefresh = await Post(otherDevice, "refresh", new { }, true);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        Assert.Equal("RESIDENT", (await SignIn(anonymous, account.Username, newPassword)).User.Role);
    }

    [Fact]
    public async Task Profile_update_accepts_only_self_service_fields_for_the_current_bearer()
    {
        using var owner = Client();
        using var other = Client();
        using var stale = Client();
        using var anonymous = Client();
        var first = await ActivatedAccount(owner);
        var second = await ActivatedAccount(other);
        var current = await SignIn(owner, first.Username);
        var unrelated = await SignIn(other, second.Username);
        var old = await SignIn(stale, first.Username);
        using var logout = await Post(stale, "logout", new { }, true);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", current.AccessToken);
        other.DefaultRequestHeaders.Authorization = new("Bearer", unrelated.AccessToken);
        stale.DefaultRequestHeaders.Authorization = new("Bearer", old.AccessToken);
        const string path = "api/v1/auth/me";
        var payload = new
        {
            displayName = "Cập nhật tên của chính mình", phoneNumber = "+84900000011", userId = unrelated.User.Id,
            username = unrelated.User.Username, email = "injected@example.invalid", role = "ADMIN",
            permissions = new[] { "MANAGE_USERS" }, status = "DISABLED", passwordHash = "injected"
        };
        using var scope = Database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var firstBefore = await ReadAccountSecurity(auth, current.User.Id);
        var secondBefore = await ReadAccountSecurity(auth, unrelated.User.Id);
        using var noActor = await anonymous.PutAsJsonAsync(path, payload);
        using var revoked = await stale.PutAsJsonAsync(path, payload);
        Assert.Equal(HttpStatusCode.Unauthorized, noActor.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal(current.User.DisplayName, await auth.UserAccounts.Where(row => row.Id == current.User.Id).Select(row => row.DisplayName).SingleAsync());
        using var updated = await owner.PutAsJsonAsync(path, payload);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var profile = (await owner.GetFromJsonAsync<AccountResponse>(path))!;
        var otherProfile = (await other.GetFromJsonAsync<AccountResponse>(path))!;
        Assert.Equal(payload.displayName, profile.DisplayName);
        Assert.Equal(payload.phoneNumber, profile.PhoneNumber);
        Assert.Equal(current.User.Id, profile.Id);
        Assert.Equal(current.User.Username, profile.Username);
        Assert.Equal(current.User.Role, profile.Role);
        Assert.Equal(current.User.Permissions.Order(), profile.Permissions.Order());
        Assert.Equal(unrelated.User.DisplayName, otherProfile.DisplayName);
        Assert.Equal(unrelated.User.PhoneNumber, otherProfile.PhoneNumber);
        AssertSameSecurity(firstBefore, await ReadAccountSecurity(auth, current.User.Id));
        AssertSameSecurity(secondBefore, await ReadAccountSecurity(auth, unrelated.User.Id));
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        Assert.Equal(2, await residents.Residents.CountAsync(row => row.UserId == current.User.Id || row.UserId == unrelated.User.Id));
    }

    [Fact]
    public async Task Avatar_upload_checks_role_session_media_and_stores_only_the_callers_avatar()
    {
        using var owner = Client();
        using var other = Client();
        using var stale = Client();
        using var anonymous = Client();
        using var staff = Client();
        var first = await ActivatedAccount(owner);
        var second = await ActivatedAccount(other);
        var current = await SignIn(owner, first.Username);
        var unrelated = await SignIn(other, second.Username);
        var old = await SignIn(stale, first.Username);
        using var logout = await Post(stale, "logout", new { }, true);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        owner.DefaultRequestHeaders.Authorization = new("Bearer", current.AccessToken);
        other.DefaultRequestHeaders.Authorization = new("Bearer", unrelated.AccessToken);
        stale.DefaultRequestHeaders.Authorization = new("Bearer", old.AccessToken);
        using var scope = Database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var now = Database.Factory.Clock.GetUtcNow();
        var staffUser = new UserAccount("avatar_" + Guid.NewGuid().ToString("N")[..12], "pending", "Kiểm thử STAFF", now,
            $"{Guid.NewGuid():N}@example.invalid", status: AccountStatus.ACTIVE, emailVerified: true);
        staffUser.ChangePasswordHash(scope.ServiceProvider.GetRequiredService<IAuthSecrets>().HashPassword(staffUser, Password), null, now);
        auth.Add(staffUser);
        await auth.SaveChangesAsync();
        access.UserRoleAssignments.Add(new UserRoleAssignment(staffUser.Id,
            await access.Roles.Where(role => role.Code == "STAFF").Select(role => role.Id).SingleAsync(), now));
        await access.SaveChangesAsync();
        staff.DefaultRequestHeaders.Authorization = new("Bearer", (await SignIn(staff, staffUser.Username)).AccessToken);

        const string path = "api/v1/auth/me/avatar";
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aG1sAAAAASUVORK5CYII=");
        async Task<HttpResponseMessage> Upload(HttpClient client, byte[] bytes, string type, Guid claimedOwner)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(type);
            form.Add(file, "file", $"{claimedOwner:N}.png");
            form.Add(new StringContent(claimedOwner.ToString()), "userId");
            return await client.PostAsync(path, form);
        }
        async Task<(string? Key, Guid? Version)> Avatar(Guid id)
        {
            var user = await auth.UserAccounts.AsNoTracking().SingleAsync(row => row.Id == id);
            return (user.AvatarStorageKey, user.AvatarVersion);
        }
        var storage = Database.Factory.Avatars;
        using var otherUpload = await Upload(other, image, "image/png", unrelated.User.Id);
        Assert.Equal(HttpStatusCode.OK, otherUpload.StatusCode);
        var otherAvatar = await Avatar(unrelated.User.Id);
        var uploaded = storage.Uploads.Count;
        var deleted = storage.Deletes.Count;
        foreach (var (actor, status) in new[]
                 {
                     (anonymous, HttpStatusCode.Unauthorized), (stale, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden)
                 })
        {
            using var denied = await Upload(actor, image, "image/png", unrelated.User.Id);
            Assert.Equal(status, denied.StatusCode);
        }
        using var wrongMime = await Upload(owner, image, "text/plain", unrelated.User.Id);
        using var wrongSignature = await Upload(owner, new byte[] { 10, 20, 30 }, "image/png", unrelated.User.Id);
        var oversized = new byte[AccountAvatarUseCases.MaxBytes + 1];
        image.CopyTo(oversized, 0);
        using var overLimit = await Upload(owner, oversized, "image/png", unrelated.User.Id);
        Assert.Equal(HttpStatusCode.BadRequest, wrongMime.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongSignature.StatusCode);
        Assert.Contains(overLimit.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        Assert.Equal(uploaded, storage.Uploads.Count);
        Assert.Equal(deleted, storage.Deletes.Count);
        Assert.Null((await Avatar(current.User.Id)).Key);
        Assert.Equal(otherAvatar, await Avatar(unrelated.User.Id));
        using var initial = await Upload(owner, image, "image/png", unrelated.User.Id);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var firstAvatar = await Avatar(current.User.Id);
        Assert.NotNull(firstAvatar.Key);
        using var replacement = await Upload(owner, image, "image/png", unrelated.User.Id);
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var finalAvatar = await Avatar(current.User.Id);
        Assert.NotEqual(firstAvatar.Version, finalAvatar.Version);
        Assert.Equal(new[] { current.User.Id, current.User.Id }, storage.Uploads.Skip(uploaded));
        Assert.Equal(firstAvatar.Key, Assert.Single(storage.Deletes.Skip(deleted)));
        Assert.False(storage.Files.ContainsKey(firstAvatar.Key!));
        Assert.Equal(current.User.Id, storage.Files[finalAvatar.Key!].Owner);
        Assert.Equal(otherAvatar, await Avatar(unrelated.User.Id));
        Assert.True(storage.Files.ContainsKey(otherAvatar.Key!));
        Assert.Equal(image, await owner.GetByteArrayAsync(path));
        Assert.Equal(HttpStatusCode.OK, await BearerStatus(current.AccessToken));
    }
}
