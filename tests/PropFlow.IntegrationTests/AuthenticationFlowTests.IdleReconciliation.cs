using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Fact]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Idle_reconciliation_closes_all_seven_paths_without_an_invocation_dispatch_or_immediate_invalidation()
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        var manager = await SeedRealtimeAccount("MANAGER");
        var resident = await SeedRealtimeAccount("RESIDENT");
        var control = await SeedRealtimeAccount("MANAGER");
        using var managerApi = Client(); using var residentApi = Client(); using var controlApi = Client();
        var m = await SignIn(managerApi, manager.Username);
        var r = await SignIn(residentApi, resident.Username);
        var valid = await SignIn(controlApi, control.Username);
        var hubs = new[] { "maintenance", "service-requests", "announcements", "resident-notifications", "notifications", "support-chat", "emergency-calls" };
        var connections = hubs.Select(hub => AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub,
            hub == "resident-notifications" ? r.AccessToken : m.AccessToken)).ToArray();
        await using var unaffected = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "notifications", valid.AccessToken);
        var queues = hubs.Select(_ => Channel.CreateUnbounded<string>()).ToArray();
        var subscriptions = connections.Select((connection, i) => connection.On<string>("Pf04Marker", marker => queues[i].Writer.TryWrite(marker))).ToArray();
        var controlQueue = Channel.CreateUnbounded<string>();
        using var controlSubscription = unaffected.On<string>("Pf04Marker", marker => controlQueue.Writer.TryWrite(marker));
        try
        {
            for (var i = 0; i < hubs.Length; i++)
            {
                await connections[i].StartAsync();
                await AuthenticationTestSessions.AwaitServerReadyAsync(connections[i]);
                await DispatchRealtimeMarker(hubs[i], [hubs[i] == "resident-notifications" ? r.User.Id.ToString() : m.User.Id.ToString()], "before", default);
                Assert.Equal("before", await queues[i].Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            }
            await unaffected.StartAsync();
            await AuthenticationTestSessions.AwaitServerReadyAsync(unaffected);
            var closed = connections.Select(AuthenticationTestSessions.WhenClosed).ToArray();
            var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
            var tokens = await auth.RefreshTokens.Where(token => (token.UserId == m.User.Id || token.UserId == r.User.Id) && token.RevokedAt == null).ToArrayAsync();
            foreach (var token in tokens) token.Revoke("pf04_idle_missed_invalidation", database.Factory.Clock.GetUtcNow());
            await auth.SaveChangesAsync();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            // No HTTP call, hub invocation, outbound dispatch or explicit registry call in this interval.
            await Task.WhenAll(closed).WaitAsync(TimeSpan.FromSeconds(35));
            output.WriteLine($"Idle reconciliation observed closure across seven paths in {elapsed.Elapsed.TotalMilliseconds:F1} ms");
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(m.AccessToken));
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(r.AccessToken));
            Assert.Equal(HttpStatusCode.OK, await BearerStatus(valid.AccessToken));
            await DispatchRealtimeMarker("notifications", [m.User.Id.ToString(), r.User.Id.ToString(), valid.User.Id.ToString()], "after", default);
            Assert.Equal("after", await controlQueue.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.All(queues, queue => Assert.False(queue.Reader.TryRead(out _)));
            Assert.Equal(HubConnectionState.Connected, unaffected.State);
        }
        finally
        {
            foreach (var subscription in subscriptions) subscription.Dispose();
            foreach (var connection in connections) await connection.DisposeAsync();
        }
    }
}
