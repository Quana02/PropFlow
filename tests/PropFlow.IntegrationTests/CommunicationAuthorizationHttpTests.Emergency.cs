using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class CommunicationAuthorizationHttpTests
{
    [Fact]
    public async Task Emergency_start_and_end_require_the_resident_or_the_selected_manager()
    {
        var residentHub = await Connect(owner, "emergency-calls");
        var managerHub = await Connect(manager, "emergency-calls");
        var start = new StartEmergencyCall(Guid.NewGuid(), residentHub.ConnectionId);
        await Deny(HttpMethod.Post, Emergency, start);
        await Reject(manager, HttpMethod.Post, Emergency, HttpStatusCode.Forbidden, start);
        await Reject(unlinked, HttpMethod.Post, Emergency, HttpStatusCode.Forbidden, start);
        await Reject(other, HttpMethod.Post, Emergency, HttpStatusCode.BadRequest, start);
        var started = await Task.WhenAll(Call<EmergencyCallView>(owner, HttpMethod.Post, Emergency, start),
            Call<EmergencyCallView>(owner, HttpMethod.Post, Emergency, start));
        var call = started[0];
        Assert.Equal(call.Id, started[1].Id);
        Assert.Equal("CONNECTING", call.Status);
        var recipients = new[] { owner.Id, manager.Id }.Order().ToArray();
        Assert.Equal(recipients, Assert.Single(realtime.Events, item => item.Kind == "call").Users);
        await residentHub.InvokeAsync("MediaConnected", call.Id, call.Revision);
        await managerHub.InvokeAsync("MediaConnected", call.Id, call.Revision);
        var path = $"{Emergency}/{call.Id}/end";
        await Deny(HttpMethod.Post, path);
        await Reject(other, HttpMethod.Post, path, HttpStatusCode.NotFound);
        await Reject(unassigned, HttpMethod.Post, path, HttpStatusCode.NotFound);
        await Reject(manager, HttpMethod.Post, $"{Emergency}/{Guid.NewGuid()}/end", HttpStatusCode.NotFound);
        var ended = await Task.WhenAll(Call<EmergencyCallView>(owner, HttpMethod.Post, path),
            Call<EmergencyCallView>(manager, HttpMethod.Post, path, new { ActorId = other.Id }));
        Assert.All(ended, row => Assert.Equal("ENDED", row.Status));
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        var row = await db.EmergencyCalls.AsNoTracking().SingleAsync();
        Assert.NotNull(row.EndedAt);
        // PostgreSQL stores timestamps at microsecond precision. The first response can retain .NET ticks.
        Assert.All(ended, response =>
            Assert.Equal(row.EndedAt!.Value.UtcTicks / 10, response.EndedAt!.Value.UtcTicks / 10));
        Assert.Contains(row.EndedBy, new Guid?[] { owner.Id, manager.Id });
        Assert.Equal(manager.Id, row.ManagerUserId);
        Assert.Equal(owner.Id, row.ResidentUserId);
        Assert.Equal(managerHub.ConnectionId, row.ManagerConnectionId);
        Assert.NotNull(row.ConnectedAt);
        var priorSignals = realtime.Events.Count(item => item.Kind == "signal");
        await Assert.ThrowsAsync<HubException>(() => managerHub.InvokeAsync("Signal",
            new EmergencySignal(call.Id, call.Revision, "ice", """{"candidate":"b5-synthetic"}""")));
        Assert.Equal(priorSignals, realtime.Events.Count(item => item.Kind == "signal"));
    }

    [Fact]
    public async Task Emergency_retry_advances_revision_without_creating_or_reviving_calls()
    {
        var residentHub = await Connect(owner, "emergency-calls");
        var managerHub = await Connect(manager, "emergency-calls");
        var otherTab = await Connect(manager, "emergency-calls");
        var call = await Call<EmergencyCallView>(owner, HttpMethod.Post, Emergency, new StartEmergencyCall(Guid.NewGuid(), residentHub.ConnectionId));
        var path = $"{Emergency}/{call.Id}/retry";
        await Deny(HttpMethod.Post, path);
        await Reject(other, HttpMethod.Post, path, HttpStatusCode.NotFound);
        await Reject(unassigned, HttpMethod.Post, path, HttpStatusCode.NotFound);
        var retries = await Task.WhenAll(
            Call<EmergencyCallView>(owner, HttpMethod.Post, path),
            Call<EmergencyCallView>(manager, HttpMethod.Post, path));
        Assert.All(retries, item => { Assert.Equal(call.Id, item.Id); Assert.Equal("CONNECTING", item.Status); });
        Assert.Equal(new[] { call.Revision + 1, call.Revision + 2 }, retries.Select(row => row.Revision).Order());
        var signal = new EmergencySignal(call.Id, call.Revision, "ice", """{"candidate":"b5-synthetic"}""");
        await Assert.ThrowsAsync<HubException>(() => managerHub.InvokeAsync("Signal", signal));
        await Assert.ThrowsAsync<HubException>(() => otherTab.InvokeAsync("Signal", signal with { Revision = call.Revision + 2 }));
        var end = Send(manager.Client, HttpMethod.Post, $"{Emergency}/{call.Id}/end");
        var retry = Send(owner.Client, HttpMethod.Post, path);
        using var ended = await end;
        using var raced = await retry;
        AssertStatus(HttpStatusCode.OK, ended);
        Assert.Contains(raced.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        await Reject(owner, HttpMethod.Post, path, HttpStatusCode.NotFound);
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        var final = await db.EmergencyCalls.SingleAsync();
        Assert.Equal("ENDED", final.Status);
        Assert.Equal(manager.Id, final.EndedBy);
        Assert.Equal(managerHub.ConnectionId, final.ManagerConnectionId);
        Assert.Equal(call.Revision + (raced.IsSuccessStatusCode ? 3 : 2), final.Revision);
    }

    [Fact]
    public async Task Rtc_configuration_is_no_store_and_only_issues_local_credentials_to_eligible_actors()
    {
        var path = Emergency + "/rtc";
        await Deny(HttpMethod.Get, path);
        Assert.Equal(0, rtcInvocations);
        await Reject(unlinked, HttpMethod.Get, path, HttpStatusCode.Forbidden);
        var before = await Snapshot();
        var fallback = await ReadRtc(owner);
        Assert.False(fallback.RelayConfigured);
        var stun = Assert.Single(fallback.IceServers);
        Assert.Null(stun.Username);
        Assert.Null(stun.Credential);
        rtc.TurnUrls = ["turn:127.0.0.1:9"];
        rtc.TurnSharedSecret = "synthetic-b5-local-signing-key";
        rtc.TurnCredentialLifetimeSeconds = 420;
        foreach (var actor in new[] { owner, manager })
        {
            var earliest = Clock.GetUtcNow().AddSeconds(420).ToUnixTimeSeconds();
            var configuration = await ReadRtc(actor);
            var latest = Clock.GetUtcNow().AddSeconds(420).ToUnixTimeSeconds();
            Assert.True(configuration.RelayConfigured);
            var turn = Assert.Single(configuration.IceServers, item => item.Username is not null);
            var parts = turn.Username!.Split(':');
            Assert.True(parts.Length == 2 && parts[1] == actor.Id.ToString("N"));
            Assert.InRange(long.Parse(parts[0]), earliest, latest);
            var expected = HMACSHA1.HashData(Encoding.UTF8.GetBytes(rtc.TurnSharedSecret), Encoding.UTF8.GetBytes(turn.Username));
            Assert.True(CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(turn.Credential!)),
                "Chữ ký TURN tổng hợp không hợp lệ.");
        }
        var issued = rtcInvocations;
        await Deny(HttpMethod.Get, path);
        Assert.Equal(issued, rtcInvocations);
        using var scope = database.Factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        await access.UserRoleAssignments.Where(row => row.UserId == manager.Id).ExecuteDeleteAsync();
        await Reject(manager, HttpMethod.Get, path, HttpStatusCode.Unauthorized);
        Assert.Equal(issued, rtcInvocations);
        Assert.Equal(before, await Snapshot());
    }

    private async Task<EmergencyRtcConfiguration> ReadRtc(Actor actor)
    {
        using var response = await Send(actor.Client, HttpMethod.Get, Emergency + "/rtc");
        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(rtc.TurnSharedSecret.Length > 0 && content.Contains(rtc.TurnSharedSecret, StringComparison.Ordinal),
            "Response RTC chứa shared secret.");
        return (await response.Content.ReadFromJsonAsync<EmergencyRtcConfiguration>())!;
    }
}
