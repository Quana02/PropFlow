using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Communication.Application.EmergencyCalls;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-13-Emergency-Voice")]
public sealed class EmergencyCallTests
{
    [Fact]
    public async Task One_online_manager_is_assigned_and_only_both_media_confirmations_mark_connected()
    {
        await using var f = new Fixture();
        await f.Service.ConnectedAsync(f.Manager, "manager", default);
        await f.Service.ConnectedAsync(f.Resident, "resident", default);
        var call = await f.Service.StartAsync(f.Resident, new(Guid.NewGuid(), "resident"), default);
        Assert.Equal("CONNECTING", call.Status);
        Assert.Equal("Manager", call.ManagerName);
        Assert.Equal("A-101 · Tầng 1", call.Apartment);
        await f.Service.MediaConnectedAsync(f.Resident, "resident", call.Id, call.Revision, default);
        Assert.Equal("CONNECTING", (await f.Service.ActiveAsync(f.Manager, "manager", default))!.Status);
        await f.Service.MediaConnectedAsync(f.Manager, "manager", call.Id, call.Revision, default);
        Assert.Equal("CONNECTED", (await f.Service.ActiveAsync(f.Resident, "resident", default))!.Status);
        await f.Service.MediaDisconnectedAsync(f.Manager, "manager", call.Id, call.Revision, default);
        Assert.Equal("CONNECTING", (await f.Service.ActiveAsync(f.Resident, "resident", default))!.Status);
        await f.Service.MediaConnectedAsync(f.Manager, "manager", call.Id, call.Revision, default);
        Assert.Equal("CONNECTED", (await f.Service.ActiveAsync(f.Resident, "resident", default))!.Status);
    }
    [Fact]
    public async Task No_manager_means_waiting_and_repeated_clicks_do_not_create_duplicate_reports()
    {
        await using var f = new Fixture();
        await f.Service.ConnectedAsync(f.Resident, "resident", default);
        var request = new StartEmergencyCall(Guid.NewGuid(), "resident");
        var call = await f.Service.StartAsync(f.Resident, request, default);
        Assert.Equal("WAITING", call.Status);
        Assert.Null(call.ManagerName);
        Assert.Equal(call.Id, (await f.Service.StartAsync(f.Resident, request, default)).Id);
        Assert.Equal(call.Id, (await f.Service.StartAsync(f.Resident, request with { ClientRequestId = Guid.NewGuid() }, default)).Id);
        Assert.Single(f.Db.EmergencyCalls);
        await f.Service.ConnectedAsync(f.Manager, "manager", default);
        Assert.Equal("CONNECTING", (await f.Service.ActiveAsync(f.Resident, "resident", default))!.Status);
    }
    [Fact]
    public async Task One_manager_does_not_receive_two_calls_and_waiting_report_is_dispatched_after_end()
    {
        await using var f = new Fixture();
        await f.Service.ConnectedAsync(f.Manager, "manager", default);
        await f.Service.ConnectedAsync(f.Resident, "resident", default);
        await f.Service.ConnectedAsync(f.OtherResident, "other", default);
        var first = await f.Service.StartAsync(f.Resident, new(Guid.NewGuid(), "resident"), default);
        var second = await f.Service.StartAsync(f.OtherResident, new(Guid.NewGuid(), "other"), default);
        Assert.Equal("WAITING", second.Status);
        await f.Service.EndAsync(f.Manager, first.Id, default);
        Assert.Equal(second.Id, (await f.Service.ActiveAsync(f.Manager, "manager", default))!.Id);
    }
    [Fact]
    public async Task Signals_are_scoped_to_assigned_users_and_the_bound_browser_connection()
    {
        await using var f = new Fixture();
        await f.Service.ConnectedAsync(f.Manager, "manager", default);
        await f.Service.ConnectedAsync(f.Resident, "resident", default);
        await f.Service.ConnectedAsync(f.OtherResident, "other", default);
        var call = await f.Service.StartAsync(f.Resident, new(Guid.NewGuid(), "resident"), default);
        var offer = new EmergencySignal(call.Id, call.Revision, "offer", """{"type":"offer","sdp":"v=0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111"}""");
        await f.Service.SignalAsync(f.Resident, "resident", offer, default);
        Assert.Equal("manager", Assert.Single(f.Push.Signals).Connection);
        Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Service.SignalAsync(f.OtherResident, "other", offer, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Service.SignalAsync(f.Manager, "manager", offer, default))).Status);
        await f.Service.ConnectedAsync(f.Resident, "another-tab", default);
        Assert.Null(await f.Service.ActiveAsync(f.Resident, "another-tab", default));
        Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Service.SignalAsync(f.Resident, "another-tab", offer, default))).Status);
    }
    [Fact]
    public async Task Disconnect_reassigns_manager_and_rejects_stale_signals_from_the_old_call_revision()
    {
        await using var f = new Fixture();
        await f.Service.ConnectedAsync(f.Manager, "manager", default);
        await f.Service.ConnectedAsync(f.Resident, "resident", default);
        var call = await f.Service.StartAsync(f.Resident, new(Guid.NewGuid(), "resident"), default);
        await f.Service.DisconnectedAsync("manager", default);
        Assert.Equal("WAITING", (await f.Service.ActiveAsync(f.Resident, "resident", default))!.Status);
        await f.Service.ConnectedAsync(f.OtherManager, "manager-two", default);
        var current = (await f.Service.ActiveAsync(f.Resident, "resident", default))!;
        Assert.True(current.Revision > call.Revision);
        Assert.Equal("Manager 2", current.ManagerName);
        Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Service.SignalAsync(f.Resident, "resident",
            new(call.Id, call.Revision, "ice", """{"candidate":"candidate:test"}"""), default))).Status);
    }
    [Fact]
    public async Task Revoked_resident_scope_and_guessed_connection_ids_cannot_start_or_end_other_calls()
    {
        await using var f = new Fixture();
        await f.Service.ConnectedAsync(f.Resident, "resident", default);
        Assert.Equal(400, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Service.StartAsync(f.OtherResident, new(Guid.NewGuid(), "resident"), default))).Status);
        var call = await f.Service.StartAsync(f.Resident, new(Guid.NewGuid(), "resident"), default);
        Assert.Equal(404, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Service.EndAsync(f.OtherResident, call.Id, default))).Status);
        f.Actors.Items.Remove(f.Resident);
        Assert.Equal(403, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Service.StartAsync(f.Resident, new(Guid.NewGuid(), null), default))).Status);
    }
    [Theory]
    [InlineData("{\"type\":4,\"sdp\":\"x\"}")]
    [InlineData("{\"type\":\"offer\",\"sdp\":\"m=video 9\"}")]
    [InlineData("{\"type\":\"offer\",\"sdp\":\"m=application 9\"}")]
    public async Task Malformed_or_non_audio_signals_are_rejected(string data)
    {
        await using var f = new Fixture();
        Assert.Equal(400, (await Assert.ThrowsAsync<SupportChatFailure>(() => f.Service.SignalAsync(f.Resident, "none", new(Guid.NewGuid(), 1, "offer", data), default))).Status);
    }
    [Fact]
    public async Task Turn_credentials_are_temporary_and_never_expose_the_shared_secret()
    {
        await using var f = new Fixture();
        var options = new EmergencyRtcOptions { TurnUrls = ["turn:relay.example.test:3478"], TurnSharedSecret = "test-only-turn-secret" };
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var config = await new EmergencyRtcConfigurationProvider(Options.Create(options), f.Actors, TimeProvider.System).GetAsync(f.Resident, default);
        var relay = config.IceServers.Single(x => x.Username is not null);
        var expires = long.Parse(relay.Username!.Split(':')[0]);
        Assert.InRange(expires, before + 590, before + 610);
        Assert.Equal(Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(options.TurnSharedSecret), Encoding.UTF8.GetBytes(relay.Username))), relay.Credential);
        Assert.DoesNotContain(options.TurnSharedSecret, System.Text.Json.JsonSerializer.Serialize(config));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Resident = Guid.NewGuid(), OtherResident = Guid.NewGuid(), Manager = Guid.NewGuid(), OtherManager = Guid.NewGuid();
        public CommunicationDbContext Db = new(new DbContextOptionsBuilder<CommunicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Directory Actors = new(); public Push Push = new(); public EmergencyCallService Service;
        public Fixture()
        {
            Actors.Items[Resident] = new(Resident, "RESIDENT", "Cư dân", Guid.NewGuid(), Apartment: "A-101 · Tầng 1");
            Actors.Items[OtherResident] = new(OtherResident, "RESIDENT", "Cư dân 2", Guid.NewGuid());
            Actors.Items[Manager] = new(Manager, "MANAGER", "Manager"); Actors.Items[OtherManager] = new(OtherManager, "MANAGER", "Manager 2");
            Service = new(Db, Actors, Push, TimeProvider.System, NullLogger<EmergencyCallService>.Instance);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Directory : ISupportChatActors
    {
        public Dictionary<Guid, SupportActor> Items = [];
        public Task<SupportActor?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
    }
    private sealed class Push : IEmergencyCallRealtime
    {
        public List<(string Connection, EmergencySignal Signal)> Signals = [];
        public Task ChangedAsync(IReadOnlyCollection<Guid> users, CancellationToken ct) => Task.CompletedTask;
        public Task SignalAsync(string connection, EmergencySignal signal, CancellationToken ct) { Signals.Add((connection, signal)); return Task.CompletedTask; }
    }
}
