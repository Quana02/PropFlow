using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Communication.Application.EmergencyCalls;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Domain.EmergencyCalls;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.Modules.Communication.Infrastructure;

public sealed class EmergencyCallService(CommunicationDbContext db, ISupportChatActors actors, IEmergencyCallRealtime realtime,
    TimeProvider clock, ILogger<EmergencyCallService> logger) : IEmergencyCalls
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private async Task<SupportActor> Actor(Guid id, CancellationToken ct)
    {
        var actor = await actors.FindAsync(id, ct);
        return actor is { Role: "MANAGER" } or { Role: "RESIDENT", ResidentId: not null } ? actor
            : throw new SupportChatFailure(403, "emergency_forbidden", "Tài khoản chưa có quyền hoặc liên kết cư trú hợp lệ.");
    }
    private IQueryable<EmergencyCall> Scoped(SupportActor actor) => actor.Role == "MANAGER"
        ? db.EmergencyCalls.Where(x => x.ManagerUserId == actor.UserId)
        : db.EmergencyCalls.Where(x => x.ResidentUserId == actor.UserId && x.ResidentId == actor.ResidentId);

    public async Task ConnectedAsync(Guid user, string connection, CancellationToken ct)
    {
        var actor = await Actor(user, ct);
        await Mutate(async users =>
        {
            var row = await db.EmergencyConnections.SingleOrDefaultAsync(x => x.Id == connection, ct);
            if (row is null) db.EmergencyConnections.Add(new(connection, user, actor.Role, Now));
            else if (row.UserId == user) row.Renew(Now);
            else throw Missing();
            await db.SaveChangesAsync(ct); await DispatchLocked(users, ct); return true;
        }, ct);
    }
    public async Task HeartbeatAsync(Guid user, string connection, CancellationToken ct)
    {
        await Actor(user, ct);
        var row = await db.EmergencyConnections.SingleOrDefaultAsync(x => x.Id == connection && x.UserId == user, ct);
        if (row is null) { await ConnectedAsync(user, connection, ct); return; }
        row.Renew(Now); await db.SaveChangesAsync(ct);
    }
    public async Task DisconnectedAsync(string connection, CancellationToken ct) => await Mutate(async users =>
    {
        var row = await db.EmergencyConnections.SingleOrDefaultAsync(x => x.Id == connection, ct);
        if (row is not null)
        {
            db.EmergencyConnections.Remove(row);
            foreach (var call in await db.EmergencyCalls.Where(x => x.Status != "ENDED" && (x.ResidentConnectionId == connection || x.ManagerConnectionId == connection)).ToArrayAsync(ct)) Touch(call, users);
            await db.SaveChangesAsync(ct);
        }
        await DispatchLocked(users, ct); return true;
    }, ct);

    public async Task<EmergencyCallView> StartAsync(Guid user, StartEmergencyCall request, CancellationToken ct)
    {
        var actor = await Actor(user, ct);
        if (actor.Role != "RESIDENT") throw new SupportChatFailure(403, "emergency_resident_only", "Chỉ cư dân được báo khẩn cấp từ chức năng này.");
        if (request.ClientRequestId == Guid.Empty) throw Invalid("Thiếu mã yêu cầu cuộc gọi.");
        return await Mutate(async users =>
        {
            if (request.ConnectionId is { } connection && !await Online(user, connection, ct)) throw Invalid("Kết nối gọi không thuộc phiên đăng nhập của bạn.");
            var replay = await db.EmergencyCalls.SingleOrDefaultAsync(x => x.ResidentUserId == user && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay is not null && replay.ResidentId != actor.ResidentId) throw Missing();
            if (replay is not null) return await View(replay, ct);
            var call = await db.EmergencyCalls.SingleOrDefaultAsync(x => x.ResidentUserId == user && x.Status != "ENDED", ct);
            if (call is not null && call.ResidentId != actor.ResidentId) throw new SupportChatFailure(409, "emergency_residence_changed", "Liên kết cư trú đã thay đổi. Hãy liên hệ trực tiếp BQL.");
            if (call is null)
            { call = new(user, actor.ResidentId!.Value, actor.Name, actor.Apartment, request.ClientRequestId, request.ConnectionId, Now); db.EmergencyCalls.Add(call); }
            else if (request.ConnectionId is { } resume) call.RebindResident(resume, Now);
            Touch(call, users); await db.SaveChangesAsync(ct); await DispatchLocked(users, ct);
            return await View(call, ct);
        }, ct);
    }

    public async Task<EmergencyCallView?> ActiveAsync(Guid user, string connection, CancellationToken ct)
    {
        var actor = await Actor(user, ct);
        if (!await Online(user, connection, ct)) throw Missing();
        var call = await Scoped(actor).AsNoTracking().SingleOrDefaultAsync(x => x.Status != "ENDED" &&
            (actor.Role == "RESIDENT" ? x.ResidentConnectionId == connection : x.ManagerConnectionId == connection), ct);
        return call is null ? null : await View(call, ct);
    }
    public async Task<EmergencyCallView> EndAsync(Guid user, Guid id, CancellationToken ct)
    {
        var actor = await Actor(user, ct);
        return await Mutate(async users =>
        {
            var call = await Scoped(actor).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            call.End(user, Now); Touch(call, users); await db.SaveChangesAsync(ct); await DispatchLocked(users, ct);
            return await View(call, ct);
        }, ct);
    }
    public async Task MediaConnectedAsync(Guid user, string connection, Guid id, int revision, CancellationToken ct)
    {
        var actor = await Actor(user, ct);
        await Mutate(async users =>
        {
            var call = await Bound(actor, connection, id, revision, ct);
            call.MediaConnected(user, Now); Touch(call, users); return true;
        }, ct);
    }
    public async Task<EmergencyCallView> RetryAsync(Guid user, Guid id, CancellationToken ct)
    {
        var actor = await Actor(user, ct);
        return await Mutate(async users =>
        {
            var call = await Scoped(actor).SingleOrDefaultAsync(x => x.Id == id && x.Status != "ENDED", ct) ?? throw Missing();
            call.Restart(Now); Touch(call, users); return await View(call, ct);
        }, ct);
    }
    public async Task MediaDisconnectedAsync(Guid user, string connection, Guid id, int revision, CancellationToken ct)
    {
        var actor = await Actor(user, ct);
        await Mutate(async users =>
        {
            var call = await Bound(actor, connection, id, revision, ct);
            call.MediaDisconnected(user, Now); Touch(call, users); return true;
        }, ct);
    }
    public async Task SignalAsync(Guid user, string connection, EmergencySignal signal, CancellationToken ct)
    {
        ValidateSignal(signal);
        var actor = await Actor(user, ct);
        var call = await Bound(actor, connection, signal.CallId, signal.Revision, ct);
        if (signal.Kind == "offer" && actor.Role != "RESIDENT" || signal.Kind == "answer" && actor.Role != "MANAGER") throw Missing();
        var recipient = actor.Role == "RESIDENT" ? call.ManagerConnectionId : call.ResidentConnectionId;
        if (recipient is null || !await db.EmergencyConnections.AnyAsync(x => x.Id == recipient && x.ExpiresAt > Now, ct))
            throw new SupportChatFailure(409, "emergency_peer_offline", "Người bên kia đang mất kết nối.");
        await realtime.SignalAsync(recipient, signal, ct);
    }
    private async Task<EmergencyCall> Bound(SupportActor actor, string connection, Guid id, int revision, CancellationToken ct)
    {
        var call = await Scoped(actor).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        var expected = actor.Role == "RESIDENT" ? call.ResidentConnectionId : call.ManagerConnectionId;
        if (call.Status is not ("CONNECTING" or "CONNECTED") || call.Revision != revision || expected != connection || !await Online(actor.UserId, connection, ct)) throw Missing();
        return call;
    }
    public async Task DispatchAsync(CancellationToken ct) => await Mutate(async users => { await DispatchLocked(users, ct); return true; }, ct);
    private async Task DispatchLocked(HashSet<Guid> users, CancellationToken ct)
    {
        var expired = await db.EmergencyConnections.Where(x => x.ExpiresAt <= Now).ToArrayAsync(ct);
        db.EmergencyConnections.RemoveRange(expired); await db.SaveChangesAsync(ct);
        var active = await db.EmergencyCalls.Where(x => x.Status != "ENDED").OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(ct);
        foreach (var call in active)
        {
            if (call.ManagerUserId is { } manager && (!await Online(manager, call.ManagerConnectionId, ct) || (await actors.FindAsync(manager, ct))?.Role != "MANAGER"))
            { Touch(call, users); call.Requeue(Now); }
            if (!await Online(call.ResidentUserId, call.ResidentConnectionId, ct))
            {
                var resume = await db.EmergencyConnections.Where(x => x.UserId == call.ResidentUserId && x.Role == "RESIDENT" && x.ExpiresAt > Now)
                    .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
                if (resume is not null) { call.RebindResident(resume.Id, Now); Touch(call, users); }
            }
            if (call.Status != "WAITING") continue;
            await db.SaveChangesAsync(ct);
            var choices = await db.EmergencyConnections.Where(x => x.Role == "MANAGER" && x.ExpiresAt > Now &&
                !db.EmergencyCalls.Any(c => c.ManagerUserId == x.UserId && c.Status != "ENDED"))
                .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(ct);
            foreach (var choice in choices)
            {
                var receiver = await actors.FindAsync(choice.UserId, ct);
                if (receiver?.Role != "MANAGER") continue;
                call.Assign(receiver.UserId, receiver.Name, choice.Id, Now); Touch(call, users); break;
            }
            await db.SaveChangesAsync(ct);
        }
    }
    private Task<bool> Online(Guid user, string? connection, CancellationToken ct) =>
        db.EmergencyConnections.AnyAsync(x => x.Id == connection && x.UserId == user && x.ExpiresAt > Now, ct);
    private async Task<EmergencyCallView> View(EmergencyCall call, CancellationToken ct) => new(call.Id, call.Status, call.Revision, call.ResidentName,
        call.Apartment, call.ManagerName, await Online(call.ResidentUserId, call.ResidentConnectionId, ct),
        call.ManagerUserId is { } manager && await Online(manager, call.ManagerConnectionId, ct), call.CreatedAt, call.EndedAt);
    private static void Touch(EmergencyCall call, HashSet<Guid> users)
    { users.Add(call.ResidentUserId); if (call.ManagerUserId is { } manager) users.Add(manager); }
    private async Task<T> Mutate<T>(Func<HashSet<Guid>, Task<T>> action, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (tx is not null) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(71241014)", ct);
        var users = new HashSet<Guid>(); var result = await action(users);
        await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        if (users.Count > 0)
            try { await realtime.ChangedAsync(users, CancellationToken.None); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning("Emergency realtime hint unavailable ({Kind}).", e.GetType().Name); }
        return result;
    }
    private static void ValidateSignal(EmergencySignal signal)
    {
        if (signal.Kind is not ("offer" or "answer" or "ice") || signal.Data is null || signal.Data.Length > 32000 || signal.Revision < 1) throw Invalid("Tín hiệu cuộc gọi không hợp lệ.");
        try
        {
            using var document = JsonDocument.Parse(signal.Data); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("Tín hiệu cuộc gọi không hợp lệ.");
            if (signal.Kind != "ice")
            {
                if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != signal.Kind || !root.TryGetProperty("sdp", out var sdp) ||
                    sdp.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(sdp.GetString()) || sdp.GetString()!.Contains("m=video", StringComparison.OrdinalIgnoreCase) ||
                    sdp.GetString()!.Contains("m=application", StringComparison.OrdinalIgnoreCase)) throw Invalid("Chỉ hỗ trợ cuộc gọi âm thanh.");
            }
            else if (!root.TryGetProperty("candidate", out var candidate) || candidate.ValueKind != JsonValueKind.String || candidate.GetString()!.Length > 4000)
                throw Invalid("Tín hiệu đường truyền không hợp lệ.");
        }
        catch (JsonException) { throw Invalid("Tín hiệu cuộc gọi không hợp lệ."); }
    }
    private static SupportChatFailure Missing() => new(404, "emergency_not_found", "Không tìm thấy cuộc gọi hoặc phiên kết nối không còn hợp lệ.");
    private static SupportChatFailure Invalid(string message) => new(400, "emergency_invalid", message);
}
