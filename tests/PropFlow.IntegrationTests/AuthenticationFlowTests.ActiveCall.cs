using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Theory]
    [MemberData(nameof(ActiveCommunicationRevocationCases))]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Active_call_retry_and_revocation_exclude_old_signaling_connection_and_preserve_history(string role, string cause)
    {
        await using var permissionRecovery = new PermissionFixtureRecovery(this);
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        await db.Database.MigrateAsync();
        var manager = await SeedRealtimeAccount("MANAGER");
        var resident = await SeedRealtimeAccount("RESIDENT");
        var outsider = await SeedRealtimeAccount("RESIDENT");
        using var managerApi = Client(); using var residentApi = Client(); using var outsiderApi = Client();
        var m = await SignIn(managerApi, manager.Username);
        var r = await SignIn(residentApi, resident.Username);
        var o = await SignIn(outsiderApi, outsider.Username);
        managerApi.DefaultRequestHeaders.Authorization = new("Bearer", m.AccessToken);
        residentApi.DefaultRequestHeaders.Authorization = new("Bearer", r.AccessToken);
        await using var managerHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", m.AccessToken, true);
        await using var residentHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", r.AccessToken, true);
        await using var outsiderHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", o.AccessToken, true);
        var managerSignals = Channel.CreateUnbounded<EmergencySignal>();
        var residentSignals = Channel.CreateUnbounded<EmergencySignal>();
        var unrelated = Channel.CreateUnbounded<EmergencySignal>();
        using var sm = managerHub.On<EmergencySignal>("EmergencySignal", item => managerSignals.Writer.TryWrite(item));
        using var sr = residentHub.On<EmergencySignal>("EmergencySignal", item => residentSignals.Writer.TryWrite(item));
        using var so = outsiderHub.On<EmergencySignal>("EmergencySignal", item => unrelated.Writer.TryWrite(item));
        await managerHub.StartAsync(); await residentHub.StartAsync(); await outsiderHub.StartAsync();
        await managerHub.InvokeAsync("Heartbeat"); await residentHub.InvokeAsync("Heartbeat");
        const string path = "api/v1/emergency-calls";
        var clientRequestId = Guid.NewGuid();
        try
        {
            using var started = await residentApi.PostAsJsonAsync(path, new StartEmergencyCall(clientRequestId, residentHub.ConnectionId));
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            var call = (await started.Content.ReadFromJsonAsync<EmergencyCallView>())!;
            await residentHub.InvokeAsync("MediaConnected", call.Id, call.Revision);
            await managerHub.InvokeAsync("MediaConnected", call.Id, call.Revision);
            Assert.Equal("CONNECTED", (await managerApi.GetFromJsonAsync<EmergencyActiveCall>(path + "/active?connectionId=" + managerHub.ConnectionId))!.Call!.Status);
            var affectedHub = role == "MANAGER" ? managerHub : residentHub;
            var peerHub = role == "MANAGER" ? residentHub : managerHub;
            var incoming = role == "MANAGER" ? managerSignals : residentSignals;
            var before = new EmergencySignal(call.Id, call.Revision, "ice", """{"candidate":"pf04-before"}""");
            await peerHub.InvokeAsync("Signal", before);
            Assert.Equal(before, await incoming.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            using var retry = await residentApi.PostAsync($"{path}/{call.Id}/retry", null);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var retrying = (await retry.Content.ReadFromJsonAsync<EmergencyCallView>())!;
            Assert.True(retrying.Revision > call.Revision);
            await Assert.ThrowsAsync<HubException>(() => peerHub.InvokeAsync("Signal", before));
            await peerHub.InvokeAsync("Signal", before with { Revision = retrying.Revision });
            Assert.Equal(retrying.Revision, (await incoming.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3))).Revision);
            var affectedApi = role == "MANAGER" ? managerApi : residentApi;
            var affected = role == "MANAGER" ? m : r;
            var account = role == "MANAGER" ? manager : resident;
            var closed = AuthenticationTestSessions.WhenClosed(affectedHub);
            var password = await RevokeRealtimeAccount(affectedApi, affected, account.Email, cause);
            if (cause is "missed-invalidation" or "effective-permission") await Assert.ThrowsAnyAsync<Exception>(() => affectedHub.InvokeAsync("Heartbeat"));
            await closed.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() => affectedHub.InvokeAsync("Signal", before with { Revision = retrying.Revision }));
            using var denied = await affectedApi.PostAsync($"{path}/{call.Id}/retry", null);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            await RestoreRealtimeAccount(affected.User.Id, role, cause);
            using var freshApi = Client();
            var fresh = await SignIn(freshApi, account.Username, password);
            freshApi.DefaultRequestHeaders.Authorization = new("Bearer", fresh.AccessToken);
            await using var freshHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", fresh.AccessToken, true);
            var recoveredSignals = Channel.CreateUnbounded<EmergencySignal>();
            using var sf = freshHub.On<EmergencySignal>("EmergencySignal", item => recoveredSignals.Writer.TryWrite(item));
            await freshHub.StartAsync(); await freshHub.InvokeAsync("Heartbeat");
            var resumed = (await freshApi.GetFromJsonAsync<EmergencyActiveCall>(path + "/active?connectionId=" + freshHub.ConnectionId))!.Call!;
            Assert.NotNull(resumed);
            Assert.Equal(call.Id, resumed.Id);
            var after = new EmergencySignal(call.Id, resumed.Revision, "ice", """{"candidate":"pf04-after-new-session"}""");
            await peerHub.InvokeAsync("Signal", after);
            Assert.Equal(after, await recoveredSignals.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(incoming.Reader.TryRead(out _));
            Assert.False(unrelated.Reader.TryRead(out _));
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(affected.AccessToken));
            using var ended = await freshApi.PostAsync($"{path}/{call.Id}/end", null);
            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
            Assert.Equal("ENDED", await db.EmergencyCalls.AsNoTracking().Where(item => item.Id == call.Id).Select(item => item.Status).SingleAsync());
        }
        finally
        {
            db.ChangeTracker.Clear();
            var remaining = await db.EmergencyCalls.SingleOrDefaultAsync(item => item.ClientRequestId == clientRequestId && item.ResidentUserId == r.User.Id);
            if (remaining is not null && remaining.Status != "ENDED")
            {
                remaining.End(r.User.Id, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            }
        }
    }
}
