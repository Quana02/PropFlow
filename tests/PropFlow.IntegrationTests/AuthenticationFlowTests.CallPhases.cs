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
    [InlineData("WAITING")]
    [InlineData("CONNECTING")]
    [InlineData("CONNECTED")]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Call_phase_revocation_preserves_the_call_and_routes_recovered_signaling_only_to_the_new_connection(string phase)
    {
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        await db.Database.MigrateAsync();
        var resident = await SeedRealtimeAccount("RESIDENT");
        var manager = await SeedRealtimeAccount("MANAGER");
        var stranger = await SeedRealtimeAccount("RESIDENT");
        using var residentApi = Client(); using var managerApi = Client(); using var strangerApi = Client();
        var r = await SignIn(residentApi, resident.Username);
        var m = await SignIn(managerApi, manager.Username);
        var other = await SignIn(strangerApi, stranger.Username);
        residentApi.DefaultRequestHeaders.Authorization = new("Bearer", r.AccessToken);
        managerApi.DefaultRequestHeaders.Authorization = new("Bearer", m.AccessToken);
        await using var residentHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", r.AccessToken, true);
        await using var managerHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", m.AccessToken, true);
        await using var strangerHub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", other.AccessToken, true);
        var oldFrames = Channel.CreateUnbounded<EmergencySignal>();
        var strangerFrames = Channel.CreateUnbounded<EmergencySignal>();
        var managerFrames = Channel.CreateUnbounded<EmergencySignal>();
        using var oldSubscription = residentHub.On<EmergencySignal>("EmergencySignal", frame => oldFrames.Writer.TryWrite(frame));
        using var strangerSubscription = strangerHub.On<EmergencySignal>("EmergencySignal", frame => strangerFrames.Writer.TryWrite(frame));
        using var managerSubscription = managerHub.On<EmergencySignal>("EmergencySignal", frame => managerFrames.Writer.TryWrite(frame));
        await residentHub.StartAsync(); await residentHub.InvokeAsync("Heartbeat"); await strangerHub.StartAsync();
        if (phase != "WAITING") { await managerHub.StartAsync(); await managerHub.InvokeAsync("Heartbeat"); }
        var requestId = Guid.NewGuid();
        const string path = "api/v1/emergency-calls";
        try
        {
            using var opened = await residentApi.PostAsJsonAsync(path, new StartEmergencyCall(requestId, residentHub.ConnectionId));
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
            var call = (await opened.Content.ReadFromJsonAsync<EmergencyCallView>())!;
            if (phase == "CONNECTED")
            {
                await residentHub.InvokeAsync("MediaConnected", call.Id, call.Revision);
                await managerHub.InvokeAsync("MediaConnected", call.Id, call.Revision);
            }
            var before = (await residentApi.GetFromJsonAsync<EmergencyActiveCall>(path + "/active?connectionId=" + residentHub.ConnectionId))!.Call!;
            Assert.Equal(phase, before.Status);
            if (phase != "WAITING")
            {
                var positive = new EmergencySignal(call.Id, call.Revision, "ice", """{"candidate":"pf04-phase-before"}""");
                await residentHub.InvokeAsync("Signal", positive);
                Assert.Equal(positive, await managerFrames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            }
            // WAITING has no selected peer: signaling is deliberately not applicable before assignment.
            var closed = AuthenticationTestSessions.WhenClosed(residentHub);
            using var logout = await Post(residentApi, "logout", new { }, true);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            await closed.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(HttpStatusCode.Unauthorized, await BearerStatus(r.AccessToken));
            Assert.Equal(HttpStatusCode.OK, await BearerStatus(other.AccessToken));
            using var rejected = await Post(residentApi, "refresh", new { }, true);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            await using var stale = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", r.AccessToken, true);
            await Assert.ThrowsAnyAsync<Exception>(() => stale.StartAsync());
            if (phase == "WAITING") { await managerHub.StartAsync(); await managerHub.InvokeAsync("Heartbeat"); }
            using var newApi = Client();
            var fresh = await SignIn(newApi, resident.Username);
            newApi.DefaultRequestHeaders.Authorization = new("Bearer", fresh.AccessToken);
            await using var recovered = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "emergency-calls", fresh.AccessToken, true);
            var recoveredFrames = Channel.CreateUnbounded<EmergencySignal>();
            using var recoveredSubscription = recovered.On<EmergencySignal>("EmergencySignal", frame => recoveredFrames.Writer.TryWrite(frame));
            await recovered.StartAsync(); await recovered.InvokeAsync("Heartbeat");
            var current = (await newApi.GetFromJsonAsync<EmergencyActiveCall>(path + "/active?connectionId=" + recovered.ConnectionId))!.Call!;
            Assert.Equal(call.Id, current.Id);
            var after = new EmergencySignal(call.Id, current.Revision, "ice", """{"candidate":"pf04-phase-after"}""");
            await managerHub.InvokeAsync("Signal", after);
            Assert.Equal(after, await recoveredFrames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(oldFrames.Reader.TryRead(out _)); Assert.False(strangerFrames.Reader.TryRead(out _));
            using var ended = await newApi.PostAsync($"{path}/{call.Id}/end", null);
            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        }
        finally
        {
            db.ChangeTracker.Clear();
            var call = await db.EmergencyCalls.SingleOrDefaultAsync(item => item.ClientRequestId == requestId && item.ResidentUserId == r.User.Id);
            if (call is not null && call.Status != "ENDED") { call.End(r.User.Id, DateTimeOffset.UtcNow); await db.SaveChangesAsync(); }
        }
    }
}
