namespace PropFlow.Modules.Communication.Domain.EmergencyCalls;

public sealed class EmergencyCall
{
    private EmergencyCall() { }
    public EmergencyCall(Guid user, Guid resident, string name, string? apartment, Guid requestId, string? connection, DateTimeOffset now)
    {
        if (user == Guid.Empty || resident == Guid.Empty || requestId == Guid.Empty) throw new ArgumentException("Thiếu thông tin cuộc gọi.");
        Id = Guid.NewGuid(); ResidentUserId = user; ResidentId = resident; ResidentName = name;
        Apartment = apartment; ClientRequestId = requestId; ResidentConnectionId = connection; CreatedAt = UpdatedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid ClientRequestId { get; private set; }
    public Guid ResidentUserId { get; private set; }
    public Guid ResidentId { get; private set; }
    public string ResidentName { get; private set; } = "";
    public string? Apartment { get; private set; }
    public string? ResidentConnectionId { get; private set; }
    public Guid? ManagerUserId { get; private set; }
    public string? ManagerName { get; private set; }
    public string? ManagerConnectionId { get; private set; }
    public string Status { get; private set; } = "WAITING";
    public int Revision { get; private set; } = 1;
    public bool ResidentMediaConnected { get; private set; }
    public bool ManagerMediaConnected { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConnectedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public Guid? EndedBy { get; private set; }

    public void Assign(Guid manager, string name, string connection, DateTimeOffset now)
    {
        if (Status != "WAITING") throw new InvalidOperationException("Cuộc gọi đã được phân công.");
        ManagerUserId = manager; ManagerName = name; ManagerConnectionId = connection;
        ResetMedia(now); Status = "CONNECTING";
    }
    public void RebindResident(string connection, DateTimeOffset now)
    {
        if (Status == "ENDED") throw new InvalidOperationException("Cuộc gọi đã kết thúc.");
        if (ResidentConnectionId == connection) return;
        ResidentConnectionId = connection; ResetMedia(now); Status = ManagerUserId is null ? "WAITING" : "CONNECTING";
    }
    public void Requeue(DateTimeOffset now)
    {
        if (Status == "ENDED" || ManagerUserId is null) return;
        ManagerUserId = null; ManagerName = ManagerConnectionId = null; ResetMedia(now); Status = "WAITING";
    }
    public void MediaConnected(Guid actor, DateTimeOffset now)
    {
        if (Status is not ("CONNECTING" or "CONNECTED")) throw new InvalidOperationException("Cuộc gọi chưa có người nhận.");
        if (actor == ResidentUserId) ResidentMediaConnected = true;
        else if (actor == ManagerUserId) ManagerMediaConnected = true;
        else throw new InvalidOperationException("Không phải người tham gia.");
        if (ResidentMediaConnected && ManagerMediaConnected) { Status = "CONNECTED"; ConnectedAt ??= now; }
        UpdatedAt = now;
    }
    public void End(Guid actor, DateTimeOffset now)
    { if (Status == "ENDED") return; Status = "ENDED"; EndedBy = actor; EndedAt = UpdatedAt = now; }
    public void MediaDisconnected(Guid actor, DateTimeOffset now)
    {
        if (Status is not ("CONNECTING" or "CONNECTED")) return;
        if (actor == ResidentUserId) ResidentMediaConnected = false;
        else if (actor == ManagerUserId) ManagerMediaConnected = false;
        else throw new InvalidOperationException("Không phải người tham gia.");
        Status = "CONNECTING"; UpdatedAt = now;
    }
    public void Restart(DateTimeOffset now)
    { if (Status == "ENDED") throw new InvalidOperationException("Cuộc gọi đã kết thúc."); ResetMedia(now); Status = ManagerUserId is null ? "WAITING" : "CONNECTING"; }
    private void ResetMedia(DateTimeOffset now)
    { Revision++; ResidentMediaConnected = ManagerMediaConnected = false; UpdatedAt = now; }
}

public sealed class EmergencyConnection
{
    private EmergencyConnection() { }
    public EmergencyConnection(string id, Guid user, string role, DateTimeOffset now)
    { Id = id; UserId = user; Role = role; CreatedAt = now; Renew(now); }
    public string Id { get; private set; } = "";
    public Guid UserId { get; private set; }
    public string Role { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public void Renew(DateTimeOffset now) => ExpiresAt = now.AddSeconds(75);
}
