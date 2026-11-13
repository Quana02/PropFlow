namespace PropFlow.Modules.Communication.Contracts;

public sealed record StartEmergencyCall(Guid ClientRequestId, string? ConnectionId);
public sealed record EmergencyActiveCall(EmergencyCallView? Call);
public sealed record EmergencyCallView(Guid Id, string Status, int Revision, string ResidentName, string? Apartment,
    string? ManagerName, bool ResidentOnline, bool ManagerOnline, DateTimeOffset CreatedAt, DateTimeOffset? EndedAt);
public sealed record EmergencySignal(Guid CallId, int Revision, string Kind, string Data);
public sealed record EmergencyIceServer(string[] Urls, string? Username = null, string? Credential = null);
public sealed record EmergencyRtcConfiguration(IReadOnlyList<EmergencyIceServer> IceServers, bool RelayConfigured);
