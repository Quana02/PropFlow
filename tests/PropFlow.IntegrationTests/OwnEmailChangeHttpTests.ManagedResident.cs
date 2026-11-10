using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.PrivilegedActions;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class OwnEmailChangeHttpTests
{
    private static RequestResidentEmailChange ManagedRequest(string target) =>
        new(target, ResidentEmailChangeReason.LostMailbox, true);
    private static string RequestPath(Guid residentId) => $"resident-accounts/{residentId}/email-change";
    private const string PendingPath = "api/v1/auth/resident-email-change";
    private const string VerifyPath = "resident-email-change/verify";

    [Fact]
    public async Task Resident_cannot_start_or_complete_a_legacy_self_service_challenge()
    {
        var (client, resident) = await Login("RESIDENT");
        using var owner = client;
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<IAuthSecrets>();
        var user = await db.UserAccounts.SingleAsync(x => x.Id == resident.Id);
        var target = Target().ToUpperInvariant();
        var binding = $"{user.Id:N}|ADMIN_EMAIL_CHANGE|{user.Email}|{target}|{secrets.HashToken(user.PasswordHash)}";
        var now = database.Factory.Clock.GetUtcNow();
        var challenge = new PrivilegedActionChallenge(user.Id, "ADMIN_EMAIL_CHANGE", secrets.HashToken(binding),
            secrets.BindOtp("123456", binding), now.AddMinutes(5), now);
        db.Add(challenge);
        await db.SaveChangesAsync();

        using var begin = await Post(owner, "email/change", new ChangeEmailRequest(target, Password));
        using var verify = await Post(owner, "email/verify", new VerifyEmailChangeRequest(challenge.Id, target, "123456"));
        using var otherPurpose = await Post(owner, VerifyPath, new VerifyResidentEmailChange(challenge.Id, target, Password, "123456"));
        Assert.Equal(HttpStatusCode.Forbidden, begin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otherPurpose.StatusCode);
        Assert.Equal(resident.Email, (await owner.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("ACCOUNTANT")]
    [InlineData("STAFF")]
    [InlineData("RESIDENT")]
    public async Task Manager_apis_reject_all_other_roles(string role)
    {
        var (client, _) = await Login(role);
        using var user = client;
        using var list = await user.GetAsync("api/v1/auth/resident-accounts");
        using var request = await Post(user, RequestPath(Guid.NewGuid()), ManagedRequest(Target()));
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, request.StatusCode);
    }

    [Fact]
    public async Task Manager_lists_resident_accounts_and_unlinked_profiles_without_exposing_internal_accounts()
    {
        var (managerClient, _) = await Login("MANAGER");
        using var manager = managerClient;
        var (residentClient, resident) = await Login("RESIDENT");
        using var residentSession = residentClient;
        var (internalClient, internalAccount) = await Login("STAFF");
        using var staff = internalClient;
        var linked = await LinkResidentAsync(resident.Id);
        var mismatched = await LinkResidentAsync(internalAccount.Id);
        var unlinked = await database.SeedRegistrationResidentAsync(Target(), currentOwnership: true);

        var page = (await manager.GetFromJsonAsync<ResidentAccountPage>("api/v1/auth/resident-accounts?pageSize=1"))!;
        Assert.Single(page.Items);
        Assert.True(page.TotalCount >= 3);
        var filtered = (await manager.GetFromJsonAsync<ResidentAccountPage>($"api/v1/auth/resident-accounts?search={linked.ContactEmail}"))!;
        var account = Assert.Single(filtered.Items);
        Assert.Equal(resident.Username, account.Username);
        Assert.Equal(resident.Email, account.AccountEmail);
        Assert.NotEmpty(account.Apartments);
        Assert.True(account.EmailVerified);
        Assert.True(account.CanRequestEmailChange);
        var mismatchedPage = (await manager.GetFromJsonAsync<ResidentAccountPage>($"api/v1/auth/resident-accounts?search={mismatched.ContactEmail}"))!;
        Assert.Null(Assert.Single(mismatchedPage.Items).Username);
        using var forbiddenTarget = await Post(manager, RequestPath(mismatched.Id), ManagedRequest(Target()));
        using var outOfScope = await Post(manager, RequestPath(Guid.NewGuid()), ManagedRequest(Target()));
        using var unlinkedTarget = await Post(manager, RequestPath(unlinked.ResidentId), ManagedRequest(Target()));
        Assert.Equal(HttpStatusCode.NotFound, forbiddenTarget.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outOfScope.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unlinkedTarget.StatusCode);
    }

    [Fact]
    public async Task Manager_records_identity_check_and_resident_completes_change_with_audit_and_session_revocation()
    {
        var (managerClient, managerAccount) = await Login("MANAGER");
        using var manager = managerClient;
        var (residentClient, resident) = await Login("RESIDENT");
        using var owner = residentClient;
        var linked = await LinkResidentAsync(resident.Id);
        using var otherSession = Client();
        using var login = await Post(otherSession, "login", new LoginRequest(resident.Username, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var target = Target();
        using var missingCsrf = await Post(manager, RequestPath(linked.Id), ManagedRequest(target), false);
        using var unconfirmed = await Post(manager, RequestPath(linked.Id), ManagedRequest(target) with { IdentityCheckedInPerson = false });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.False(database.Factory.Mail.Codes.ContainsKey(target));
        using var sent = await Post(manager, RequestPath(linked.Id), ManagedRequest(target));
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        Assert.Empty(await sent.Content.ReadAsStringAsync());
        Assert.Equal(resident.Email, (await owner.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
        var pending = (await owner.GetFromJsonAsync<ResidentEmailChangePending>(PendingPath))!;
        var code = database.Factory.Mail.Codes[target];
        var verify = new VerifyResidentEmailChange(pending.ChallengeId!.Value, target, Password, code);
        using var managerCannotVerify = await Post(manager, VerifyPath, verify);
        Assert.Equal(HttpStatusCode.Forbidden, managerCannotVerify.StatusCode);
        var (otherResident, _) = await Login("RESIDENT");
        using var other = otherResident;
        using var wrongAccount = await Post(other, VerifyPath, verify);
        using var wrongPassword = await Post(owner, VerifyPath, verify with { CurrentPassword = "wrong" });
        using var wrongOtp = await Post(owner, VerifyPath, verify with { Code = code == "111111" ? "222222" : "111111" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongAccount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongOtp.StatusCode);
        using var completed = await Post(owner, VerifyPath, verify);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(otherSession, "refresh", new { })).StatusCode);
        using var fresh = Client();
        using var signedIn = await Post(fresh, "login", new LoginRequest(resident.Username, Password));
        var session = (await signedIn.Content.ReadFromJsonAsync<SessionResponse>())!;
        fresh.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        Assert.Equal(target.ToUpperInvariant(), session.User.Email);
        Assert.True(session.User.EmailVerified);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(fresh, VerifyPath, verify)).StatusCode);

        using var scope = database.Factory.Services.CreateScope();
        var profile = await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Residents.SingleAsync(r => r.Id == linked.Id);
        Assert.Equal(linked.ContactEmail, profile.Email);
        Assert.Equal(resident.Id, profile.UserId);
        var audit = await scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>().PrivilegedActionChallenges
            .SingleAsync(item => item.Id == pending.ChallengeId);
        Assert.Equal(managerAccount.Id, audit.RequestedBy);
        Assert.Equal(resident.Id, audit.UserId);
        Assert.Equal(linked.Id, audit.ResidentId);
        Assert.Equal(nameof(ResidentEmailChangeReason.LostMailbox), audit.Reason);
        Assert.Equal("VERIFIED", audit.Outcome);
        Assert.NotNull(audit.UsedAt);
        Assert.True(audit.CreatedAt <= audit.UsedAt && audit.UsedAt < audit.ExpiresAt);
        Assert.NotEqual(code, audit.CodeHash);
        Assert.DoesNotContain(target, audit.PayloadHash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resident_verification_requires_authentication()
    {
        using var anonymous = Client();
        using var pending = await anonymous.GetAsync(PendingPath);
        using var verify = await Post(anonymous, VerifyPath, new VerifyResidentEmailChange(Guid.NewGuid(), Target(), Password, "123456"));
        Assert.Equal(HttpStatusCode.Unauthorized, pending.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("locked")]
    [InlineData("disabled")]
    [InlineData("manager_locked")]
    [InlineData("manager_permission_revoked")]
    public async Task Managed_request_cannot_complete_after_expiry_or_loss_of_eligibility(string scenario)
    {
        var (managerClient, actor) = await Login("MANAGER");
        using var manager = managerClient;
        var (residentClient, account) = await Login("RESIDENT");
        using var resident = residentClient;
        var linked = await LinkResidentAsync(account.Id);
        var target = Target();
        Assert.Equal(HttpStatusCode.NoContent, (await Post(manager, RequestPath(linked.Id), ManagedRequest(target))).StatusCode);
        var pending = (await resident.GetFromJsonAsync<ResidentEmailChangePending>(PendingPath))!;
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var administration = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var grant = await administration.RolePermissions.SingleAsync(mapping =>
            mapping.Role.Code == "MANAGER" && mapping.Permission.Code == ResidentAccountPolicies.RequestEmailChange);
        var original = database.Factory.Clock.Offset;
        try
        {
            if (scenario == "expired") database.Factory.Clock.Offset = original.Add(TimeSpan.FromMinutes(6));
            else if (scenario == "manager_permission_revoked")
            {
                administration.RolePermissions.Remove(grant);
                await administration.SaveChangesAsync();
            }
            else
            {
                var id = scenario == "manager_locked" ? actor.Id : account.Id;
                var user = await db.UserAccounts.SingleAsync(x => x.Id == id);
                if (scenario == "disabled") user.Disable(null, database.Factory.Clock.GetUtcNow());
                else user.Lock(null, database.Factory.Clock.GetUtcNow());
                await db.SaveChangesAsync();
            }
            using var rejected = await Post(resident, VerifyPath,
                new VerifyResidentEmailChange(pending.ChallengeId!.Value, target, Password, database.Factory.Mail.Codes[target]));
            Assert.Equal(scenario is "locked" or "disabled" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(account.Email, await db.UserAccounts.Where(x => x.Id == account.Id).Select(x => x.Email).SingleAsync());
            if (scenario == "manager_permission_revoked")
            {
                var audit = await db.PrivilegedActionChallenges.SingleAsync(item => item.Id == pending.ChallengeId);
                Assert.Equal(actor.Id, audit.RequestedBy);
                Assert.Equal("INELIGIBLE", audit.Outcome);
                Assert.NotNull(audit.UsedAt);
            }
        }
        finally
        {
            database.Factory.Clock.Offset = original;
            if (scenario == "manager_permission_revoked" && administration.Entry(grant).State == EntityState.Detached)
            {
                administration.RolePermissions.Add(grant);
                await administration.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Replacement_from_another_manager_cancels_the_previous_request()
    {
        var (firstClient, firstActor) = await Login("MANAGER");
        using var firstManager = firstClient;
        var (secondClient, secondActor) = await Login("MANAGER");
        using var secondManager = secondClient;
        var (residentClient, account) = await Login("RESIDENT");
        using var resident = residentClient;
        var linked = await LinkResidentAsync(account.Id);
        var oldTarget = Target();
        using var sent = await Post(firstManager, RequestPath(linked.Id), ManagedRequest(oldTarget));
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        var previous = (await resident.GetFromJsonAsync<ResidentEmailChangePending>(PendingPath))!;
        var oldCode = database.Factory.Mail.Codes[oldTarget];
        var original = database.Factory.Clock.Offset;
        try
        {
            database.Factory.Clock.Offset = original.Add(TimeSpan.FromSeconds(61));
            var newTarget = Target();
            using var replacement = await Post(secondManager, RequestPath(linked.Id), ManagedRequest(newTarget));
            Assert.Equal(HttpStatusCode.NoContent, replacement.StatusCode);
            var current = (await resident.GetFromJsonAsync<ResidentEmailChangePending>(PendingPath))!;
            Assert.NotEqual(previous.ChallengeId, current.ChallengeId);

            using var stale = await Post(resident, VerifyPath,
                new VerifyResidentEmailChange(previous.ChallengeId!.Value, oldTarget, Password, oldCode));
            Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
            Assert.Equal(account.Email, (await resident.GetFromJsonAsync<AccountResponse>("api/v1/auth/me"))!.Email);
            using var completed = await Post(resident, VerifyPath,
                new VerifyResidentEmailChange(current.ChallengeId!.Value, newTarget, Password, database.Factory.Mail.Codes[newTarget]));
            Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);

            using var scope = database.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var cancelled = await db.PrivilegedActionChallenges.SingleAsync(item => item.Id == previous.ChallengeId);
            var verified = await db.PrivilegedActionChallenges.SingleAsync(item => item.Id == current.ChallengeId);
            Assert.Equal(firstActor.Id, cancelled.RequestedBy);
            Assert.Equal("SUPERSEDED", cancelled.Outcome);
            Assert.NotNull(cancelled.UsedAt);
            Assert.Equal(secondActor.Id, verified.RequestedBy);
            Assert.Equal("VERIFIED", verified.Outcome);
        }
        finally { database.Factory.Clock.Offset = original; }
    }

    [Fact]
    public async Task Two_residents_cannot_claim_the_same_new_email()
    {
        var (managerClient, _) = await Login("MANAGER");
        var (secondManagerClient, _) = await Login("MANAGER");
        using var manager = managerClient;
        using var secondManager = secondManagerClient;
        var (firstClient, firstAccount) = await Login("RESIDENT");
        var (secondClient, secondAccount) = await Login("RESIDENT");
        using var first = firstClient;
        using var second = secondClient;
        var firstLink = await LinkResidentAsync(firstAccount.Id);
        var secondLink = await LinkResidentAsync(secondAccount.Id);
        var target = Target();
        Assert.Equal(HttpStatusCode.NoContent, (await Post(manager, RequestPath(firstLink.Id), ManagedRequest(target))).StatusCode);
        var firstPending = (await first.GetFromJsonAsync<ResidentEmailChangePending>(PendingPath))!;
        var firstCode = database.Factory.Mail.Codes[target];
        Assert.Equal(HttpStatusCode.NoContent, (await Post(secondManager, RequestPath(secondLink.Id), ManagedRequest(target))).StatusCode);
        var secondPending = (await second.GetFromJsonAsync<ResidentEmailChangePending>(PendingPath))!;
        var secondCode = database.Factory.Mail.Codes[target];
        var results = await Task.WhenAll(
            Post(first, VerifyPath, new VerifyResidentEmailChange(firstPending.ChallengeId!.Value, target, Password, firstCode)),
            Post(second, VerifyPath, new VerifyResidentEmailChange(secondPending.ChallengeId!.Value, target.ToUpperInvariant(), Password, secondCode)));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
        var (losingManager, losingLink) = results[0].StatusCode == HttpStatusCode.Conflict ? (manager, firstLink) : (secondManager, secondLink);
        using var duplicate = await Post(losingManager, RequestPath(losingLink.Id), ManagedRequest(target));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private async Task<(Guid Id, string ContactEmail)> LinkResidentAsync(Guid userId)
    {
        var contact = Target();
        var seed = await database.SeedRegistrationResidentAsync(contact, currentOwnership: true);
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var resident = await db.Residents.SingleAsync(r => r.Id == seed.ResidentId);
        resident.LinkUserAccount(userId, null, database.Factory.Clock.GetUtcNow());
        await db.SaveChangesAsync();
        return (resident.Id, contact);
    }
}
