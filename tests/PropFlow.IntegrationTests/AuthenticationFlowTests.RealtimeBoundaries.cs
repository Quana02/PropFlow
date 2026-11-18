using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Presentation;
using PropFlow.Modules.Maintenance.Presentation.Hubs;
using PropFlow.Api.Composition;

namespace PropFlow.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    public static IEnumerable<object[]> RealtimePermissionCases() => RealtimeRevocationCases()
        .Where(row => (string)row[2] == "logout").Select(row => new[] { row[0], row[1] });

    [Theory]
    [MemberData(nameof(RealtimePermissionCases))]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Effective_permission_loss_without_invalidation_blocks_live_delivery_and_old_credentials(string hub, string role)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        var account = await SeedRealtimeAccount(role);
        using var browser = Client();
        using var independentBrowser = Client();
        var session = await SignIn(browser, account.Username);
        var independentSession = await SignIn(independentBrowser, account.Username);
        browser.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        await using var oldHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, session.AccessToken);
        var queue = Channel.CreateUnbounded<string>();
        using var subscription = oldHub.On<string>("Pf04Marker", marker => queue.Writer.TryWrite(marker));
        var oldNotices = Channel.CreateUnbounded<FinanceNotificationUpdate>();
        using var oldNoticeSubscription = oldHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", notice => oldNotices.Writer.TryWrite(notice));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await oldHub.StartAsync(deadline.Token);
        var alternateRole = hub == "resident-notifications" ? "RESIDENT" : role == "MANAGER"
            ? hub is "maintenance" or "service-requests" ? "STAFF" : "RESIDENT" : "MANAGER";
        var control = await SeedRealtimeAccount(alternateRole);
        var foreign = await SeedRealtimeAccount(alternateRole);
        using var controlApi = Client(); using var foreignApi = Client();
        var validControl = await SignIn(controlApi, control.Username);
        var foreignSession = await SignIn(foreignApi, foreign.Username);
        await using var tabB = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, session.AccessToken);
        await using var tabC = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, independentSession.AccessToken);
        await using var controlHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, validControl.AccessToken);
        await using var foreignHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, foreignSession.AccessToken);
        foreach (var connection in new[] { tabB, tabC, controlHub, foreignHub }) await connection.StartAsync(deadline.Token);
        using var business = await PrepareRealtimeBusiness(hub, role, [oldHub, tabB, tabC, controlHub, foreignHub], session.User, validControl.User, alternateRole);
        if (business is not null) await business.Emit(false, "effective-permission");
        await DispatchRealtimeMarker(hub, [session.User.Id.ToString()], "before", deadline.Token);
        Assert.Equal("before", await queue.Reader.ReadAsync(deadline.Token));
        if (hub is "notifications" or "resident-notifications")
        {
            var id = await PublishPermissionNotice("before");
            Assert.Equal(id, (await oldNotices.Reader.ReadAsync(deadline.Token)).Id);
        }
        var access = services.GetRequiredService<AdministrationDbContext>();
        // Fixed RBAC has no grant-management endpoint. This is a controlled database fixture,
        // Prefer an unrelated grant; a single-grant role also exercises loss of hub admission.
        var candidates = await access.RolePermissions.Include(item => item.Permission).Include(item => item.Role)
            .Where(item => item.Role.Code == role).ToArrayAsync();
        var removed = candidates.FirstOrDefault(item => item.Permission.Code is not
            ("PERFORM_ASSIGNED_OPERATIONS" or "USE_RESIDENT_SERVICES" or "MANAGE_OPERATIONS")) ?? candidates[0];
        access.RolePermissions.Remove(removed);
        await access.SaveChangesAsync(deadline.Token);
        try
        {
            var closed = AuthenticationTestSessions.WhenClosed(oldHub);
            if (business is not null)
            {
                await business.Emit(true, "effective-permission");
                Assert.Equal(HttpStatusCode.OK, await BearerStatus(validControl.AccessToken));
            }
            await DispatchRealtimeMarker(hub, [session.User.Id.ToString()], "after-loss", deadline.Token);
            await closed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(queue.Reader.TryRead(out _));
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(session.AccessToken));
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(independentSession.AccessToken));
            using var refresh = await Post(browser, "refresh", new { }, true);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            using var independentRefresh = await Post(independentBrowser, "refresh", new { }, true);
            Assert.Equal(HttpStatusCode.Unauthorized, independentRefresh.StatusCode);
            var auth = services.GetRequiredService<AuthenticationDbContext>();
            Assert.All(await auth.RefreshTokens.AsNoTracking().Where(item => item.UserId == session.User.Id).ToArrayAsync(), item => Assert.NotNull(item.RevokedAt));
            await using var stale = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, session.AccessToken, true);
            await Assert.ThrowsAnyAsync<Exception>(() => stale.StartAsync(deadline.Token));
            using var newBrowser = Client();
            var current = await SignIn(newBrowser, account.Username);
            Assert.DoesNotContain(removed.Permission.Code, current.User.Permissions);
            await using var newHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, current.AccessToken);
            var received = Channel.CreateUnbounded<string>();
            using var currentSubscription = newHub.On<string>("Pf04Marker", marker => received.Writer.TryWrite(marker));
            var currentNotices = Channel.CreateUnbounded<FinanceNotificationUpdate>();
            using var currentNoticeSubscription = newHub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", notice => currentNotices.Writer.TryWrite(notice));
            var lostAdmission = hub == "maintenance" && role == "STAFF"
                || hub is "support-chat" or "emergency-calls" && role == "RESIDENT";
            if (lostAdmission)
                await Assert.ThrowsAnyAsync<Exception>(() => newHub.StartAsync(deadline.Token));
            else
            {
                await newHub.StartAsync(deadline.Token);
                await DispatchRealtimeMarker(hub, [session.User.Id.ToString()], "current-permissions", deadline.Token);
                Assert.Equal("current-permissions", await received.Reader.ReadAsync(deadline.Token));
                if (hub is "notifications" or "resident-notifications")
                {
                    var id = await PublishPermissionNotice("after");
                    Assert.Equal(id, (await currentNotices.Reader.ReadAsync(deadline.Token)).Id);
                    Assert.False(oldNotices.Reader.TryRead(out _));
                }
            }
            Assert.False(queue.Reader.TryRead(out _));
        }
        finally
        {
            access.RolePermissions.Add(new RolePermission(removed.RoleId, removed.PermissionId, removed.CreatedAt, removed.CreatedBy));
            await access.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(session.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(independentSession.AccessToken));

        async Task<Guid> PublishPermissionNotice(string label)
        {
            var sourceId = Guid.NewGuid();
            await services.GetRequiredService<IFinanceNotificationStore>().PublishAsync(new FinanceNotification(sourceId,
                session.User.Id, null, "PF04 permission " + label, "Synthetic private notice", "", "PAYMENT", sourceId, "/resident/payments"), default);
            return await services.GetRequiredService<CommunicationDbContext>().Notifications.AsNoTracking()
                .Where(item => item.SourceEventId == sourceId && item.RecipientUserId == session.User.Id).Select(item => item.Id).SingleAsync();
        }
    }

    [Theory]
    [InlineData("maintenance")]
    [InlineData("support-chat")]
    [InlineData("emergency-calls")]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Hub_filter_rejects_a_real_invocation_before_missed_invalidation_can_close_transport(string hub)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        var account = await SeedRealtimeAccount("MANAGER");
        using var browser = Client();
        var session = await SignIn(browser, account.Username);
        await using var connection = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, session.AccessToken, true);
        await connection.StartAsync();
        await InvokeRealtimeControl(connection, hub);
        var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken).Claims.Single(claim => claim.Type == "sid").Value);
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var token = await db.RefreshTokens.SingleAsync(item => item.SessionId == sid && item.RevokedAt == null);
        token.Revoke("pf04_filter_boundary", database.Factory.Clock.GetUtcNow());
        await db.SaveChangesAsync();
        // No outbound send, registry notification or reconciliation is used to trigger the check.
        var hubType = hub switch { "maintenance" => typeof(MaintenanceHub), "support-chat" => typeof(SupportChatHub), _ => typeof(EmergencyCallHub) };
        var caller = new Pf04Caller(connection.ConnectionId!, new ClaimsPrincipal(new ClaimsIdentity(
            new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken).Claims, "test")));
        using var instance = (Hub)ActivatorUtilities.CreateInstance(scope.ServiceProvider, hubType);
        var method = hubType.GetMethod(hub == "maintenance" ? "JoinMaintenanceUpdates" : "Heartbeat")!;
        var context = new HubInvocationContext(caller, scope.ServiceProvider, instance, method, []);
        // Resolve the production filter. Its type is internal; no production visibility is changed for testing.
        var filterType = typeof(IRealtimeSessionValidation).Assembly.GetType("PropFlow.Api.Composition.SessionHubFilter", true)!;
        var filter = (IHubFilter)scope.ServiceProvider.GetRequiredService(filterType);
        var nextCalled = false;
        var error = await Assert.ThrowsAsync<HubException>(() => filter.InvokeMethodAsync(context, _ =>
        { nextCalled = true; return ValueTask.FromResult<object?>(null); }).AsTask());
        Assert.Contains("Phiên đăng nhập không còn hiệu lực.", error.Message);
        Assert.False(nextCalled);
        Assert.True(caller.Aborted);
        await Assert.ThrowsAnyAsync<Exception>(() => InvokeRealtimeControl(connection, hub));
        Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(session.AccessToken));
    }

    private sealed class Pf04Caller(string id, ClaimsPrincipal user) : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId => id;
        public override string? UserIdentifier => user.FindFirst("sub")?.Value;
        public override ClaimsPrincipal User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    [Theory]
    [MemberData(nameof(RealtimePermissionCases))]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Natural_access_expiration_does_not_revoke_the_active_refresh_session(string hub, string role)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        var account = await SeedRealtimeAccount(role);
        using var browser = Client();
        var session = await SignIn(browser, account.Username);
        var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken).Claims.Single(claim => claim.Type == "sid").Value);
        var secrets = scope.ServiceProvider.GetRequiredService<IAuthSecrets>();
        var lifetime = session.ExpiresAt - database.Factory.Clock.GetUtcNow();
        var expiring = secrets.Issue(session.User, DateTimeOffset.UtcNow - lifetime + TimeSpan.FromSeconds(3), sid);
        await using var connection = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, expiring.AccessToken);
        var oldFrames = Channel.CreateUnbounded<string>();
        using var oldSubscription = connection.On<string>("Pf04Marker", marker => oldFrames.Writer.TryWrite(marker));
        var closed = AuthenticationTestSessions.WhenClosed(connection);
        await connection.StartAsync();
        await DispatchRealtimeMarker(hub, [session.User.Id.ToString()], "before-expiry", default);
        Assert.Equal("before-expiry", await oldFrames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        await closed.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(expiring.AccessToken));
        await using var stale = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, expiring.AccessToken, true);
        await Assert.ThrowsAnyAsync<Exception>(() => stale.StartAsync());
        using var refresh = await Post(browser, "refresh", new { }, true);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<SessionResponse>())!;
        await using var recovered = AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub, rotated.AccessToken);
        var currentFrames = Channel.CreateUnbounded<string>();
        using var currentSubscription = recovered.On<string>("Pf04Marker", marker => currentFrames.Writer.TryWrite(marker));
        await recovered.StartAsync();
        Assert.Equal(HubConnectionState.Connected, recovered.State);
        await DispatchRealtimeMarker(hub, [session.User.Id.ToString()], "after-expiry", default);
        Assert.Equal("after-expiry", await currentFrames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(oldFrames.Reader.TryRead(out _));
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        Assert.True(await db.RefreshTokens.AnyAsync(item => item.SessionId == sid && item.RevokedAt == null));
    }
}
