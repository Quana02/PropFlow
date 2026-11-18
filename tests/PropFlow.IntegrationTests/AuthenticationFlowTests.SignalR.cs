using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Presentation;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Communication.Presentation;
using PropFlow.Modules.Maintenance.Presentation.Hubs;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Presentation.Hubs;

namespace PropFlow.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    private RolePermission? removedPermissionFixture;

    private sealed class PermissionFixtureRecovery(AuthenticationFlowTests owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(owner.RestorePermissionFixture());
    }

    private async Task RestorePermissionFixture()
    {
        if (removedPermissionFixture is not { } removed) return;
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        access.RolePermissions.Add(removed);
        await access.SaveChangesAsync();
        removedPermissionFixture = null;
    }
    public static IEnumerable<object[]> RealtimeRevocationCases()
    {
        var hubs = new Dictionary<string, string[]>
        {
            ["maintenance"] = ["MANAGER", "STAFF"],
            ["service-requests"] = ["MANAGER", "STAFF", "RESIDENT"],
            ["announcements"] = ["MANAGER", "RESIDENT"],
            ["resident-notifications"] = ["RESIDENT"],
            ["notifications"] = ["ADMIN", "MANAGER", "STAFF", "ACCOUNTANT", "RESIDENT"],
            ["support-chat"] = ["MANAGER", "RESIDENT"],
            ["emergency-calls"] = ["MANAGER", "RESIDENT"]
        };
        foreach (var (hub, roles) in hubs)
        foreach (var role in roles)
        {
            foreach (var cause in new[] { "logout", "password-change", "password-reset", "email-change", "missed-invalidation" })
                yield return [hub, role, cause];
            if (role is "MANAGER" or "STAFF" or "ACCOUNTANT")
                foreach (var cause in new[] { "disable", "role-change" }) yield return [hub, role, cause];
        }
    }

    [Theory]
    [MemberData(nameof(RealtimeRevocationCases))]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task SignalR_revocation_rejects_stale_inbound_outbound_and_reconnect(string hub, string role, string cause)
    {
        await using (var scope = database.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        }
        var account = await SeedRealtimeAccount(role);
        var controlAccount = await SeedRealtimeAccount(role);
        var foreignAccount = await SeedRealtimeAccount(role);
        using var first = Client();
        using var independent = Client();
        using var control = Client();
        using var foreign = Client();
        var current = await SignIn(first, account.Username);
        var independentSession = await SignIn(independent, account.Username);
        var controlSession = await SignIn(control, controlAccount.Username);
        var foreignSession = await SignIn(foreign, foreignAccount.Username);
        first.DefaultRequestHeaders.Authorization = new("Bearer", current.AccessToken);

        await using var tabA = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, current.AccessToken);
        await using var tabB = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, current.AccessToken);
        await using var otherDevice = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, independentSession.AccessToken);
        await using var valid = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, controlSession.AccessToken);
        await using var excluded = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, foreignSession.AccessToken);
        var targets = new[] { tabA, tabB, otherDevice, valid, excluded };
        var channels = targets.Select(_ => Channel.CreateUnbounded<string>()).ToArray();
        var subscriptions = targets.Select((connection, index) => connection.On<string>("Pf04Marker",
            marker => channels[index].Writer.TryWrite(marker))).ToArray();
        var notices = targets.Select(_ => Channel.CreateUnbounded<FinanceNotificationUpdate>()).ToArray();
        var noticeSubscriptions = targets.Select((connection, index) => connection.On<FinanceNotificationUpdate>("FinanceNotificationCreated",
            notice => notices[index].Writer.TryWrite(notice))).ToArray();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            foreach (var target in targets) await target.StartAsync(deadline.Token);
            await InvokeRealtimeControl(tabA, hub);
            using var business = await PrepareRealtimeBusiness(hub, role, targets, current.User, controlSession.User);
            if (business is not null) await business.Emit(false, cause);
            var before = Guid.NewGuid().ToString("N");
            await DispatchRealtimeMarker(hub, [current.User.Id.ToString(), controlSession.User.Id.ToString()], before, deadline.Token);
            foreach (var channel in channels.Take(4)) Assert.Equal(before, await channel.Reader.ReadAsync(deadline.Token));
            Assert.False(channels[4].Reader.TryRead(out _));
            if (hub is "notifications" or "resident-notifications") await VerifyPersistedNotice("before-revoke", false);

            var closeA = AuthenticationTestSessions.WhenClosed(tabA);
            var closeB = AuthenticationTestSessions.WhenClosed(tabB);
            var closeIndependent = AuthenticationTestSessions.WhenClosed(otherDevice);
            var elapsed = Stopwatch.StartNew();
            var loginPassword = await RevokeRealtimeAccount(first, current, account.Email, cause);
            var committedMs = elapsed.Elapsed.TotalMilliseconds;
            var after = Guid.NewGuid().ToString("N");
            // Goes through the production hub lifetime manager after the security change committed.
            await DispatchRealtimeMarker(hub, [current.User.Id.ToString(), controlSession.User.Id.ToString()], after, deadline.Token);
            Assert.Equal(after, await channels[3].Reader.ReadAsync(deadline.Token));
            if (cause == "logout") Assert.Equal(after, await channels[2].Reader.ReadAsync(deadline.Token));
            else await closeIndependent.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.WhenAll(closeA, closeB).WaitAsync(TimeSpan.FromSeconds(2));
            var blockedMs = elapsed.Elapsed.TotalMilliseconds;
            Assert.False(channels[0].Reader.TryRead(out _));
            Assert.False(channels[1].Reader.TryRead(out _));
            Assert.False(channels[4].Reader.TryRead(out _));
            if (cause != "logout") Assert.False(channels[2].Reader.TryRead(out _));
            if (hub is "notifications" or "resident-notifications") await VerifyPersistedNotice("after-revoke", true);
            if (business is not null) await business.Emit(true, cause);
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(current.AccessToken));
            Assert.Equal(HttpStatusCode.OK, await BearerStatus(controlSession.AccessToken));
            Assert.Equal(cause == "logout" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
                await BearerStatus(independentSession.AccessToken));
            using var refresh = await Post(first, "refresh", new { }, true);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            if (cause != "logout")
            {
                using var independentRefresh = await Post(independent, "refresh", new { }, true);
                Assert.Equal(HttpStatusCode.Unauthorized, independentRefresh.StatusCode);
            }
            await Assert.ThrowsAnyAsync<Exception>(() => InvokeRealtimeControl(tabA, hub));
            await using var stale = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, current.AccessToken, negotiate: true);
            await Assert.ThrowsAnyAsync<Exception>(() => stale.StartAsync(deadline.Token));

            await using (var scope = database.Factory.Services.CreateAsyncScope())
            {
                var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
                var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(current.AccessToken).Claims.Single(claim => claim.Type == "sid").Value);
                var rows = await auth.RefreshTokens.AsNoTracking().Where(token => token.UserId == current.User.Id).ToArrayAsync();
                Assert.All(rows.Where(token => cause != "logout" || token.SessionId == sid), token => Assert.NotNull(token.RevokedAt));
            }
            await RestoreRealtimeAccount(current.User.Id, role, cause);
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(current.AccessToken));
            using var freshBrowser = Client();
            var fresh = await SignIn(freshBrowser, account.Username, loginPassword);
            await using var freshHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, fresh.AccessToken);
            await freshHub.StartAsync(deadline.Token);
            Assert.Equal(HubConnectionState.Connected, freshHub.State);
            output.WriteLine($"PF04 hub={hub} role={role} cause={cause} commitResponseMs={committedMs:F1} blockedMs={blockedMs:F1} privatePositiveControl=true");
        }
        finally
        {
            foreach (var subscription in subscriptions.Concat(noticeSubscriptions)) subscription.Dispose();
        }

        async Task VerifyPersistedNotice(string label, bool afterRevoke)
        {
            await using var scope = database.Factory.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IFinanceNotificationStore>();
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var recipients = new[] { current.User.Id, controlSession.User.Id };
            for (var i = 0; i < recipients.Length; i++)
                await store.PublishAsync(new FinanceNotification(ids[i], recipients[i], null, "PF04 " + label,
                    "Synthetic private notification " + ids[i], "", "PAYMENT", ids[i], "/resident/payments"), default);
            var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
            var persisted = await db.Notifications.AsNoTracking().Where(item => ids.Contains(item.SourceEventId!.Value))
                .ToDictionaryAsync(item => item.RecipientUserId);
            Assert.Equal(2, persisted.Count);
            var expected = afterRevoke ? (cause == "logout" ? new[] { 2, 3 } : new[] { 3 }) : new[] { 0, 1, 2, 3 };
            foreach (var i in expected)
            {
                var notice = await notices[i].Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(persisted[i == 3 ? controlSession.User.Id : current.User.Id].Id, notice.Id);
                Assert.Equal("PF04 " + label, notice.Title);
            }
            for (var i = 0; i < notices.Length; i++) Assert.False(notices[i].Reader.TryRead(out _));
        }
    }

    private async Task<(string Username, string Email)> SeedRealtimeAccount(string role)
    {
        if (role == "RESIDENT")
        {
            using var browser = Client();
            var account = await ActivatedAccount(browser);
            return (account.Username, account.Email);
        }
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var roles = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<IAuthSecrets>();
        var suffix = Guid.NewGuid().ToString("N");
        var now = database.Factory.Clock.GetUtcNow();
        var user = new UserAccount("pf04_" + suffix, "pending", "PF04 synthetic account", now,
            suffix + "@example.invalid", status: AccountStatus.ACTIVE, emailVerified: true);
        user.ChangePasswordHash(secrets.HashPassword(user, Password), null, now);
        auth.UserAccounts.Add(user);
        await auth.SaveChangesAsync();
        var roleId = await roles.Roles.Where(item => item.Code == role).Select(item => item.Id).SingleAsync();
        roles.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, now));
        await roles.SaveChangesAsync();
        return (user.Username, user.Email!);
    }

    private static Task InvokeRealtimeControl(HubConnection connection, string hub) => hub switch
    {
        "maintenance" => connection.InvokeAsync("JoinMaintenanceUpdates"),
        "support-chat" or "emergency-calls" => connection.InvokeAsync("Heartbeat"),
        // Subscription-only hubs have no public invocation. Stopped transport must reject send attempts.
        _ => connection.SendAsync("PF04_UnregisteredMethod")
    };

    private Task DispatchRealtimeMarker(string hub, string[] users, string marker, CancellationToken ct)
    {
        var services = database.Factory.Services;
        return hub switch
        {
            "maintenance" => services.GetRequiredService<IHubContext<MaintenanceHub>>().Clients.Users(users).SendAsync("Pf04Marker", marker, ct),
            "service-requests" => services.GetRequiredService<IHubContext<ServiceRequestHub>>().Clients.Users(users).SendAsync("Pf04Marker", marker, ct),
            "announcements" => services.GetRequiredService<IHubContext<AnnouncementHub>>().Clients.Users(users).SendAsync("Pf04Marker", marker, ct),
            "support-chat" => services.GetRequiredService<IHubContext<SupportChatHub>>().Clients.Users(users).SendAsync("Pf04Marker", marker, ct),
            "emergency-calls" => services.GetRequiredService<IHubContext<EmergencyCallHub>>().Clients.Users(users).SendAsync("Pf04Marker", marker, ct),
            _ => services.GetRequiredService<IHubContext<ResidentNotificationHub>>().Clients.Users(users).SendAsync("Pf04Marker", marker, ct)
        };
    }

    private async Task<string> RevokeRealtimeAccount(HttpClient browser, SessionResponse current, string email, string cause)
    {
        const string replacement = "PF04 replacement passphrase 42!";
        HttpResponseMessage response;
        switch (cause)
        {
            case "logout":
                response = await Post(browser, "logout", new { }, true);
                break;
            case "password-change":
                response = await Post(browser, "password/change", new ChangePasswordRequest(Password, replacement), true);
                break;
            case "password-reset":
                var challenge = await RecoveryChallenge(browser, email);
                var proof = await VerifyRecovery(browser, challenge.ChallengeId, database.Factory.Mail.Codes[email]);
                Assert.Equal(HttpStatusCode.OK, await BearerStatus(current.AccessToken));
                response = await Post(browser, "password/reset", new ResetPasswordRequest(proof.ChallengeId, proof.Proof, replacement), true);
                break;
            case "email-change":
                var target = Guid.NewGuid().ToString("N") + "@example.invalid";
                if (current.User.Role == "RESIDENT")
                {
                    var manager = await SeedRealtimeAccount("MANAGER");
                    using var actor = Client();
                    var managerSession = await SignIn(actor, manager.Username);
                    actor.DefaultRequestHeaders.Authorization = new("Bearer", managerSession.AccessToken);
                    await using var scope = database.Factory.Services.CreateAsyncScope();
                    var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
                    var id = await residents.Residents.Where(item => item.UserId == current.User.Id).Select(item => item.Id).SingleAsync();
                    using var request = await Post(actor, $"resident-accounts/{id}/email-change",
                        new RequestResidentEmailChange(target, ResidentEmailChangeReason.LostMailbox, true), true);
                    Assert.Equal(HttpStatusCode.NoContent, request.StatusCode);
                    Assert.Equal(HttpStatusCode.OK, await BearerStatus(current.AccessToken));
                    var pending = (await browser.GetFromJsonAsync<ResidentEmailChangePending>("api/v1/auth/resident-email-change"))!;
                    response = await Post(browser, "resident-email-change/verify",
                        new VerifyResidentEmailChange(pending.ChallengeId!.Value, target, Password, database.Factory.Mail.Codes[target]), true);
                }
                else
                {
                    using var request = await Post(browser, "email/change", new ChangeEmailRequest(target, Password), true);
                    Assert.Equal(HttpStatusCode.OK, request.StatusCode);
                    Assert.Equal(HttpStatusCode.OK, await BearerStatus(current.AccessToken));
                    var pending = (await request.Content.ReadFromJsonAsync<ChallengeResponse>())!;
                    response = await Post(browser, "email/verify", new VerifyEmailChangeRequest(pending.ChallengeId, target, database.Factory.Mail.Codes[target]), true);
                }
                break;
            case "disable":
            case "role-change":
                var admin = await SeedRealtimeAccount("ADMIN");
                using (var actor = Client())
                {
                    var adminSession = await SignIn(actor, admin.Username);
                    actor.DefaultRequestHeaders.Authorization = new("Bearer", adminSession.AccessToken);
                    var operation = cause == "disable" ? "status" : "role";
                    object payload = cause == "disable" ? new ChangeStatusRequest("DISABLED") : new ChangeRoleRequest("ACCOUNTANT");
                    if (current.User.Role == "ACCOUNTANT" && cause == "role-change") payload = new ChangeRoleRequest("STAFF");
                    response = await actor.PutAsJsonAsync($"api/v1/administration/internal-accounts/{current.User.Id}/{operation}", payload);
                }
                break;
            case "effective-permission":
                await using (var scope = database.Factory.Services.CreateAsyncScope())
                {
                    var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
                    var candidates = await access.RolePermissions.Include(item => item.Permission).Include(item => item.Role)
                        .Where(item => item.Role.Code == current.User.Role).ToArrayAsync();
                    var removed = candidates.FirstOrDefault(item => item.Permission.Code is not
                        ("PERFORM_ASSIGNED_OPERATIONS" or "USE_RESIDENT_SERVICES" or "MANAGE_OPERATIONS")) ?? candidates[0];
                    access.RolePermissions.Remove(removed);
                    await access.SaveChangesAsync();
                    removedPermissionFixture = new RolePermission(removed.RoleId, removed.PermissionId, removed.CreatedAt, removed.CreatedBy);
                }
                // A refresh with the now-stale fingerprint durably revokes that session before restoration.
                using (var rejected = await Post(browser, "refresh", new { }, true))
                    Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
                return Password;
            default:
                await using (var scope = database.Factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
                    var rows = await db.RefreshTokens.Where(token => token.UserId == current.User.Id && token.RevokedAt == null).ToArrayAsync();
                    foreach (var row in rows) row.Revoke("pf04_missed_invalidation", database.Factory.Clock.GetUtcNow());
                    await db.SaveChangesAsync();
                }
                return Password;
        }
        using (response) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return cause is "password-change" or "password-reset" ? replacement : Password;
    }

    private async Task RestoreRealtimeAccount(Guid userId, string originalRole, string cause)
    {
        if (cause == "effective-permission") { await RestorePermissionFixture(); return; }
        if (cause is not ("disable" or "role-change")) return;
        var admin = await SeedRealtimeAccount("ADMIN");
        using var actor = Client();
        var session = await SignIn(actor, admin.Username);
        actor.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        object payload = cause == "disable" ? new ChangeStatusRequest("ACTIVE") : new ChangeRoleRequest(originalRole);
        using var result = await actor.PutAsJsonAsync($"api/v1/administration/internal-accounts/{userId}/{(cause == "disable" ? "status" : "role")}", payload);
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
    }
}
