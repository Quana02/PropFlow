using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-01"), Trait("UseCase", "FE-01.6")]
public sealed partial class OwnEmailChangeHttpTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    private const string Password = "A secure passphrase 123!";
    private HttpClient Client() => database.Factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private async Task<(HttpClient Client, AccountResponse Account)> Login(string role = "ADMIN")
    {
        using var scope = database.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var user = new UserAccount("email-" + Guid.NewGuid().ToString("N"), "pending", "Tài khoản kiểm thử",
            database.Factory.Clock.GetUtcNow(), $"old-{Guid.NewGuid():N}@example.invalid", status: AccountStatus.ACTIVE, emailVerified: true);
        user.ChangePasswordHash(scope.ServiceProvider.GetRequiredService<IAuthSecrets>().HashPassword(user, Password), null,
            database.Factory.Clock.GetUtcNow());
        auth.UserAccounts.Add(user);
        await auth.SaveChangesAsync();
        var assigned = await access.Roles.SingleAsync(item => item.Code == role);
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, assigned.Id, database.Factory.Clock.GetUtcNow()));
        await access.SaveChangesAsync();
        var client = Client();
        var login = await Post(client, "login", new LoginRequest(user.Username, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return (client, session.User);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/" + path) { Content = JsonContent.Create(body) };
        if (csrf)
        {
            var token = await client.GetFromJsonAsync<CsrfResponse>("api/v1/auth/csrf");
            request.Headers.Add("X-CSRF-TOKEN", token!.Token);
        }
        return await client.SendAsync(request);
    }

    private static string Target() => $"new-{Guid.NewGuid():N}@example.invalid";
    private static async Task<ChallengeResponse> Request(HttpClient client, string target)
    {
        var response = await Post(client, "email/change", new ChangeEmailRequest(target, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!;
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("MANAGER")]
    [InlineData("STAFF")]
    [InlineData("ACCOUNTANT")]
    public async Task Own_email_changes_only_after_new_mailbox_verification_and_old_sessions_are_rejected(string role)
    {
        var (client, account) = await Login(role);
        using (client)
        {
            using var otherSession = Client();
            using var secondLogin = await Post(otherSession, "login", new LoginRequest(account.Username, Password));
            Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
            var target = Target();
            var recovery = await Post(client, "password/forgot", new ForgotPasswordRequest(account.Email!));
            var reset = (await recovery.Content.ReadFromJsonAsync<ChallengeResponse>())!;
            var oldCode = database.Factory.Mail.Codes[account.Email!];
            var challenge = await Request(client, target);
            // Only the new mailbox receives the email-change code.
            Assert.Equal(oldCode, database.Factory.Mail.Codes[account.Email!]);
            Assert.Equal(account.Email, (await client.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);

            var verify = await Post(client, "email/verify", new VerifyEmailChangeRequest(challenge.ChallengeId, target,
                database.Factory.Mail.Codes[target]));
            Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/v1/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "refresh", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(otherSession, "refresh", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "password/verify", new VerifyChallengeRequest(reset.ChallengeId, oldCode))).StatusCode);

            var login = await Post(client, "login", new LoginRequest(account.Username, Password));
            var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
            Assert.Equal(target.ToUpperInvariant(), session.User.Email);
            Assert.True(session.User.EmailVerified);
            Assert.Equal(account.Id, session.User.Id);
            Assert.Equal(account.Username, session.User.Username);
            Assert.Equal(role, session.User.Role);
            Assert.Equal(account.Permissions.Order(), session.User.Permissions.Order());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/verify", new VerifyEmailChangeRequest(
                challenge.ChallengeId, target, database.Factory.Mail.Codes[target]))).StatusCode);
        }
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected_by_both_endpoints()
    {
        using var client = Client();
        using var request = await Post(client, "email/change", new ChangeEmailRequest(Target(), Password));
        using var verify = await Post(client, "email/verify", new VerifyEmailChangeRequest(Guid.NewGuid(), Target(), "123456"));
        Assert.Equal(HttpStatusCode.Unauthorized, request.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    [Fact]
    public async Task Supplied_account_and_resident_ids_do_not_select_a_different_account()
    {
        var (requester, own) = await Login("STAFF");
        var (otherClient, other) = await Login("RESIDENT");
        using (requester)
        using (otherClient)
        {
            var target = Target();
            using var request = await Post(requester, "email/change",
                new { newEmail = target, currentPassword = Password, userId = other.Id, residentId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
            var challenge = (await request.Content.ReadFromJsonAsync<ChallengeResponse>())!;
            using var verify = await Post(requester, "email/verify",
                new { challenge.ChallengeId, newEmail = target, code = database.Factory.Mail.Codes[target], userId = other.Id, residentId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
            using var scope = database.Factory.Services.CreateScope();
            var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            Assert.Equal(target.ToUpperInvariant(), (await auth.UserAccounts.SingleAsync(user => user.Id == own.Id)).Email);
            Assert.Equal(other.Email, (await otherClient.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
        }
    }

    [Fact]
    public async Task Missing_csrf_wrong_password_same_email_and_duplicate_email_do_not_issue_codes()
    {
        var (client, account) = await Login();
        using (client)
        {
            var target = Target();
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/change", new ChangeEmailRequest(target, Password), false)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/change", new ChangeEmailRequest(target, "wrong"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/change", new ChangeEmailRequest("invalid-email", Password))).StatusCode);
            Assert.False(database.Factory.Mail.Codes.ContainsKey(target));
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/change", new ChangeEmailRequest(account.Email!, Password))).StatusCode);
            var (otherClient, other) = await Login();
            using (otherClient)
                Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "email/change", new ChangeEmailRequest(other.Email!, Password))).StatusCode);
        }
    }

    [Fact]
    public async Task Codes_are_bound_to_requester_and_target_and_stop_after_five_failures()
    {
        var (client, account) = await Login("MANAGER");
        var (other, _) = await Login("ACCOUNTANT");
        using (client)
        using (other)
        {
            var target = Target();
            var challenge = await Request(client, target);
            var code = database.Factory.Mail.Codes[target];
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/verify", new VerifyEmailChangeRequest(challenge.ChallengeId, target, code), false)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(other, "email/verify", new VerifyEmailChangeRequest(challenge.ChallengeId, target, code))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/verify", new VerifyEmailChangeRequest(challenge.ChallengeId, Target(), code))).StatusCode);
            var wrong = code == "000000" ? "111111" : "000000";
            for (var i = 0; i < 4; i++)
                Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/verify", new VerifyEmailChangeRequest(challenge.ChallengeId, target, wrong))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/verify", new VerifyEmailChangeRequest(challenge.ChallengeId, target, code))).StatusCode);
            Assert.Equal(account.Email, (await client.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
        }
    }

    [Fact]
    public async Task Resend_invalidates_previous_code_and_expired_code_cannot_change_email()
    {
        var (client, account) = await Login();
        var originalOffset = database.Factory.Clock.Offset;
        using (client)
        try
        {
            var target = Target();
            var first = await Request(client, target);
            var firstCode = database.Factory.Mail.Codes[target];
            Assert.Equal(HttpStatusCode.TooManyRequests, (await Post(client, "email/change", new ChangeEmailRequest(target, Password))).StatusCode);
            database.Factory.Clock.Offset = originalOffset.Add(TimeSpan.FromSeconds(61));
            var next = await Request(client, target);
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/verify", new VerifyEmailChangeRequest(first.ChallengeId, target, firstCode))).StatusCode);
            database.Factory.Clock.Offset = originalOffset.Add(TimeSpan.FromMinutes(7));
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "email/verify", new VerifyEmailChangeRequest(next.ChallengeId, target, database.Factory.Mail.Codes[target]))).StatusCode);
            Assert.Equal(account.Email, (await client.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
        }
        finally { database.Factory.Clock.Offset = originalOffset; }
    }

    [Fact]
    public async Task Concurrent_accounts_cannot_claim_the_same_canonical_email()
    {
        var (first, firstAccount) = await Login("MANAGER");
        var (second, secondAccount) = await Login("ACCOUNTANT");
        using (first)
        using (second)
        {
            var target = Target();
            var firstChallenge = await Request(first, target);
            var firstCode = database.Factory.Mail.Codes[target];
            var secondChallenge = await Request(second, target);
            var secondCode = database.Factory.Mail.Codes[target];
            var results = await Task.WhenAll(
                Post(first, "email/verify", new VerifyEmailChangeRequest(firstChallenge.ChallengeId, target, firstCode)),
                Post(second, "email/verify", new VerifyEmailChangeRequest(secondChallenge.ChallengeId, target.ToUpperInvariant(), secondCode)));
            Assert.Single(results.Where(item => item.StatusCode == HttpStatusCode.NoContent));
            Assert.Single(results.Where(item => item.StatusCode == HttpStatusCode.Conflict));
            var loser = results[0].StatusCode == HttpStatusCode.Conflict ? first : second;
            var originalEmail = results[0].StatusCode == HttpStatusCode.Conflict ? firstAccount.Email : secondAccount.Email;
            Assert.Equal(originalEmail, (await loser.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lockout_or_locked_account_cannot_finish_or_request_email_change(bool lockedStatus)
    {
        var (client, account) = await Login("STAFF");
        using (client)
        {
            var target = Target();
            var challenge = await Request(client, target);
            using var scope = database.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var user = await db.UserAccounts.SingleAsync(item => item.Id == account.Id);
            var now = database.Factory.Clock.GetUtcNow();
            if (lockedStatus) user.Lock(null, now);
            else user.RecordLoginFailure(1, TimeSpan.FromMinutes(15), now);
            await db.SaveChangesAsync();

            var expected = lockedStatus ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest;
            using var verify = await Post(client, "email/verify",
                new VerifyEmailChangeRequest(challenge.ChallengeId, target, database.Factory.Mail.Codes[target]));
            using var request = await Post(client, "email/change", new ChangeEmailRequest(Target(), Password));
            Assert.Equal(expected, verify.StatusCode);
            Assert.Equal(expected, request.StatusCode);
            await db.Entry(user).ReloadAsync();
            Assert.Equal(account.Email, user.Email);
        }
    }

    [Fact]
    public async Task Verification_waits_for_a_concurrent_account_lock_and_rechecks_status()
    {
        var (client, account) = await Login("MANAGER");
        using (client)
        {
            var target = Target();
            var challenge = await Request(client, target);
            using var scope = database.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            await using var statusWrite = await db.Database.BeginTransactionAsync();
            var user = await db.UserAccounts.SingleAsync(item => item.Id == account.Id);
            user.Lock(null, database.Factory.Clock.GetUtcNow());
            await db.SaveChangesAsync();

            var verification = Post(client, "email/verify",
                new VerifyEmailChangeRequest(challenge.ChallengeId, target, database.Factory.Mail.Codes[target]));
            using var observerScope = database.Factory.Services.CreateScope();
            var observer = observerScope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var waitingForStatusWrite = false;
            while (!waitingForStatusWrite && !verification.IsCompleted && DateTime.UtcNow < deadline)
            {
                waitingForStatusWrite = await observer.Database.SqlQueryRaw<bool>("""
                    SELECT EXISTS (
                        SELECT 1 FROM pg_stat_activity
                        WHERE datname = current_database()
                            AND wait_event_type = 'Lock'
                            AND query LIKE '%FROM auth.users WHERE id = %FOR UPDATE%'
                    ) AS "Value"
                    """).SingleAsync();
                if (!waitingForStatusWrite) await Task.Delay(25);
            }

            await statusWrite.CommitAsync();
            using var response = await verification.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(waitingForStatusWrite);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await db.Entry(user).ReloadAsync();
            Assert.Equal(account.Email, user.Email);
        }
    }

    [Fact]
    public async Task Changing_the_current_email_invalidates_a_pending_code_even_with_a_fresh_session()
    {
        var (client, account) = await Login("ACCOUNTANT");
        using (client)
        {
            var target = Target();
            var challenge = await Request(client, target);
            var replacement = Target();
            using var scope = database.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var user = await db.UserAccounts.SingleAsync(item => item.Id == account.Id);
            user.ChangeEmail(replacement, account.Id, database.Factory.Clock.GetUtcNow());
            await db.SaveChangesAsync();

            client.DefaultRequestHeaders.Authorization = null;
            using var login = await Post(client, "login", new LoginRequest(account.Username, Password));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
            using var verify = await Post(client, "email/verify",
                new VerifyEmailChangeRequest(challenge.ChallengeId, target, database.Factory.Mail.Codes[target]));
            Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
            Assert.Equal(replacement.ToUpperInvariant(), (await client.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
        }
    }

    [Fact]
    public async Task Hourly_resend_limit_applies_across_target_addresses()
    {
        var (client, _) = await Login("ACCOUNTANT");
        using (client)
        {
            var originalOffset = database.Factory.Clock.Offset;
            try
            {
                for (var i = 0; i < 5; i++)
                {
                    database.Factory.Clock.Offset = originalOffset.Add(TimeSpan.FromSeconds(61 * i));
                    await Request(client, Target());
                }
                database.Factory.Clock.Offset = originalOffset.Add(TimeSpan.FromSeconds(305));
                var target = Target();
                using var limited = await Post(client, "email/change", new ChangeEmailRequest(target, Password));
                Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
                Assert.False(database.Factory.Mail.Codes.ContainsKey(target));
            }
            finally { database.Factory.Clock.Offset = originalOffset; }
        }
    }

    [Fact]
    public async Task Failed_delivery_cancels_challenge_and_does_not_change_email()
    {
        var (client, account) = await Login();
        using (client)
        {
            database.Factory.Mail.FailNext = true;
            var target = Target();
            var response = await Post(client, "email/change", new ChangeEmailRequest(target, Password));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.False(database.Factory.Mail.Codes.ContainsKey(target));
            Assert.Equal(account.Email, (await client.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
            using var scope = database.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var challenge = await db.PrivilegedActionChallenges.SingleAsync(item => item.UserId == account.Id);
            Assert.NotNull(challenge.UsedAt);
        }
    }
}
