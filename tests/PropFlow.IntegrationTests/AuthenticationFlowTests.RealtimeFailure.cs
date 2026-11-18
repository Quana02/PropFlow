using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Infrastructure;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class TemporaryPostgresFactAttribute : FactAttribute
{
    public TemporaryPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROPFLOW_PF04_CLUSTER")))
            Skip = "Requires an explicitly owned temporary PostgreSQL cluster; application servers must never be stopped.";
    }
}

public sealed partial class AuthenticationFlowTests
{
    [TemporaryPostgresFact]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Temporary_database_interruption_fails_closed_on_all_paths_and_recovers_without_reviving_revoked_sessions()
    {
        var cluster = Path.GetFullPath(Environment.GetEnvironmentVariable("PROPFLOW_PF04_CLUSTER")!);
        var owner = Environment.GetEnvironmentVariable("PROPFLOW_PF04_CLUSTER_OWNER");
        var pgCtl = Environment.GetEnvironmentVariable("PROPFLOW_PF04_PG_CTL");
        Assert.False(string.IsNullOrWhiteSpace(owner));
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()).ToUpperInvariant(), cluster.ToUpperInvariant());
        Assert.StartsWith("propflow-pf04-", Path.GetFileName(cluster));
        Assert.Equal(owner, (await File.ReadAllTextAsync(Path.Combine(cluster, "pf04-owner"))).Trim());
        Assert.True(File.Exists(pgCtl));
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var auth = services.GetRequiredService<AuthenticationDbContext>();
        var cfg = new NpgsqlConnectionStringBuilder(auth.Database.GetConnectionString());
        Assert.Equal("127.0.0.1", cfg.Host);
        Assert.Equal(45488, cfg.Port);
        Assert.StartsWith("propflow_fe01_test_", cfg.Database);
        await services.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        var manager = await SeedRealtimeAccount("MANAGER");
        var resident = await SeedRealtimeAccount("RESIDENT");
        using var managerBrowser = Client();
        using var residentBrowser = Client();
        var revoked = await SignIn(managerBrowser, manager.Username);
        var valid = await SignIn(residentBrowser, resident.Username);
        var hubs = new[] { "maintenance", "service-requests", "announcements", "resident-notifications", "notifications", "support-chat", "emergency-calls" };
        var connections = hubs.Select(hub => AuthenticationTestSessions.ConnectWebSocket(database.Factory, hub,
            hub == "resident-notifications" ? valid.AccessToken : revoked.AccessToken)).ToArray();
        var queues = hubs.Select(_ => Channel.CreateUnbounded<string>()).ToArray();
        var subscriptions = connections.Select((hub, i) => hub.On<string>("Pf04Marker", marker => queues[i].Writer.TryWrite(marker))).ToArray();
        var stopped = false;
        var failedNotice = Guid.NewGuid();
        try
        {
            for (var i = 0; i < hubs.Length; i++)
            {
                await connections[i].StartAsync();
                await AuthenticationTestSessions.AwaitServerReadyAsync(connections[i]);
                await DispatchRealtimeMarker(hubs[i], [hubs[i] == "resident-notifications" ? valid.User.Id.ToString() : revoked.User.Id.ToString()], "available", default);
                Assert.Equal("available", await queues[i].Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            }
            var sid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(revoked.AccessToken).Claims.Single(claim => claim.Type == "sid").Value);
            var token = await auth.RefreshTokens.SingleAsync(item => item.SessionId == sid && item.RevokedAt == null);
            token.Revoke("pf04_before_database_interruption", database.Factory.Clock.GetUtcNow());
            await auth.SaveChangesAsync();
            await ControlPostgres("stop");
            stopped = true;
            NpgsqlConnection.ClearAllPools();
            var validSid = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(valid.AccessToken).Claims.Single(claim => claim.Type == "sid").Value);
            await AssertDatabaseUnavailable(() => services.GetRequiredService<IAccessSessionReader>()
                .FindAsync(valid.User.Id, validSid, default));
            await AssertDatabaseUnavailable(() => services.GetRequiredService<IAccountAccess>()
                .GetAsync(valid.User.Id, default));
            await AssertDatabaseUnavailable(() => services.GetRequiredService<IFinanceNotificationStore>()
                .PublishAsync(new FinanceNotification(failedNotice, valid.User.Id, null, "PF04 interrupted",
                    "Synthetic private data", "", "PAYMENT", failedNotice, "/resident/payments"), default));
            foreach (var hub in hubs)
                await DispatchRealtimeMarker(hub, [revoked.User.Id.ToString(), valid.User.Id.ToString()], "must-not-leak", default);
            Assert.All(queues, queue => Assert.False(queue.Reader.TryRead(out _)));
            // The session filter runs before any hub method or presence write even when the socket was open.
            await Assert.ThrowsAnyAsync<Exception>(() => connections[0].InvokeAsync("JoinMaintenanceUpdates"));
            using (var request = Client())
            {
                request.DefaultRequestHeaders.Authorization = new("Bearer", valid.AccessToken);
                using var denied = await request.GetAsync("api/v1/auth/me");
                Assert.False(denied.IsSuccessStatusCode);
                var error = await denied.Content.ReadAsStringAsync();
                Assert.DoesNotContain("Npgsql", error, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Host=", error, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Password=", error, StringComparison.OrdinalIgnoreCase);
            }
            await ControlPostgres("start");
            stopped = false;
            NpgsqlConnection.ClearAllPools();
            Assert.Equal(HttpStatusCode.OK, await BearerStatus(valid.AccessToken));
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(revoked.AccessToken));
            Assert.False(await services.GetRequiredService<CommunicationDbContext>().Notifications
                .AnyAsync(item => item.SourceEventId == failedNotice));
            await DispatchRealtimeMarker("resident-notifications", [valid.User.Id.ToString()], "recovered", default);
            Assert.Equal("recovered", await queues[3].Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            foreach (var hub in hubs.Where(hub => hub != "resident-notifications"))
                await DispatchRealtimeMarker(hub, [revoked.User.Id.ToString()], "still-denied", default);
            Assert.All(queues, queue => Assert.False(queue.Reader.TryRead(out _)));
            await using var stale = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "notifications", revoked.AccessToken, true);
            await Assert.ThrowsAnyAsync<Exception>(() => stale.StartAsync());
            using var freshBrowser = Client();
            var fresh = await SignIn(freshBrowser, manager.Username);
            await using var current = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "notifications", fresh.AccessToken);
            await current.StartAsync();
            Assert.Equal(HubConnectionState.Connected, current.State);
        }
        finally
        {
            if (stopped) await ControlPostgres("start");
            foreach (var subscription in subscriptions) subscription.Dispose();
            foreach (var connection in connections) await connection.DisposeAsync();
        }

        async Task ControlPostgres(string action)
        {
            // On Windows, shell launch isolates vstest's inherited handles from the long-lived server.
            // Redirecting pg_ctl's stdout/stderr alone still leaves other inheritable runner handles.
            var shellLaunch = OperatingSystem.IsWindows();
            var info = new ProcessStartInfo(pgCtl!)
            {
                UseShellExecute = shellLaunch, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = !shellLaunch, RedirectStandardError = !shellLaunch,
                RedirectStandardInput = !shellLaunch
            };
            foreach (var argument in new[] { "-D", Path.Combine(cluster, "data"), "-w", "-t", "15" }) info.ArgumentList.Add(argument);
            if (action == "stop") { info.ArgumentList.Add("-m"); info.ArgumentList.Add("fast"); }
            else
            {
                foreach (var argument in new[] { "-l", Path.Combine(cluster, "postgres.log"), "-o", "-h 127.0.0.1 -p 45488 -c max_connections=180" })
                    info.ArgumentList.Add(argument);
            }
            info.ArgumentList.Add(action);
            using var process = Process.Start(info)!;
            output.WriteLine($"Owned temporary PostgreSQL {action}: pg_ctl PID={process.Id}");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, process.ExitCode);
        }

        static async Task AssertDatabaseUnavailable(Func<Task> operation)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(operation);
            // EF may wrap Npgsql's transient connection exception in InvalidOperationException.
            var driverFailure = false;
            for (Exception? current = error; current is not null; current = current.InnerException)
                driverFailure |= current is NpgsqlException;
            Assert.True(driverFailure, "Expected a PostgreSQL connection failure in the exception chain.");
        }
    }
}
