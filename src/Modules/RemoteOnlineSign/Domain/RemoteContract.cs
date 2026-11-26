namespace PropFlow.Modules.RemoteOnlineSign.Domain;

public enum RemoteContractStatus { DRAFT, PENDING_INVESTOR_SIGNATURE, INVESTOR_SIGNED_DRAFT, PENDING_RESIDENT_SIGNATURE, COMPLETED, REJECTED, CANCELLED, EXPIRED }
public enum RemoteSigningMode { INVESTOR_AND_RESIDENT = 0, PRE_SIGNED_INVESTOR = 1, UNSELECTED = 2 }
public enum SignatureRole { INVESTOR, RESIDENT }
public enum SignatureMethod { DRAW, UPLOAD, TYPE }
public sealed class RemoteContract
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string ContractCode { get; set; } = ""; public string ContractName { get; set; } = ""; public Guid CreatorId { get; set; } public Guid? ResidentId { get; set; }
    public RemoteContractStatus Status { get; set; } = RemoteContractStatus.DRAFT; public RemoteSigningMode SigningMode { get; set; }
    public DateTimeOffset? Deadline { get; set; } public int Revision { get; set; } = 1; public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset MetadataUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? WorkingStorageKey { get; set; } public string? WorkingSha256 { get; set; }
    public uint Version { get; set; }
}
public sealed class ContractCodeCounter { public DateOnly BusinessDate { get; set; } public int LastNumber { get; set; } }
public sealed class AnnualContractCodeCounter { public int BusinessYear { get; set; } public int LastNumber { get; set; } }
public sealed class ContractDocument { public Guid Id { get; set; }=Guid.NewGuid(); public Guid ContractId { get; set; } public string StorageKey { get; set; }=""; public string Sha256 { get; set; }=""; public long Size { get; set; } public string MimeType { get; set; }="application/pdf"; public int PageCount { get; set; } public string PageMetadata { get; set; }="[]"; public bool IsFinal { get; set; } public DateTimeOffset CreatedAt { get; set; }=DateTimeOffset.UtcNow; }
public sealed class ContractSignatureField { public Guid Id { get; set; }=Guid.NewGuid(); public Guid ContractId { get; set; } public SignatureRole Role { get; set; } public int Page { get; set; } public decimal X { get; set; } public decimal Y { get; set; } public decimal Width { get; set; } public decimal Height { get; set; } }
public sealed class ContractSigningSession
{
    public Guid Id { get; set; }=Guid.NewGuid(); public Guid ContractId { get; set; } public Guid SignerId { get; set; } public SignatureRole Role { get; set; }
    // Retained for additive compatibility with the initial Remote schema. Remote Phase 4
    // delegates OTP proof to Authentication and does not write plaintext OTP values here.
    public string? OtpHash { get; set; } public DateTimeOffset? ExpiresAt { get; set; } public int Attempts { get; set; } public int Resends { get; set; } public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? AuthChallengeId { get; set; } public SignatureMethod Method { get; set; }
    public byte[]? SignatureImage { get; set; } public string? TypedSignatureText { get; set; } public string? TypedSignatureFont { get; set; }
    public string SignatureSha256 { get; set; }=""; public string BindingHash { get; set; }="";
    public DateTimeOffset? OtpVerifiedAt { get; set; } public DateTimeOffset? FinalizationStartedAt { get; set; } public DateTimeOffset? FinalizedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }=DateTimeOffset.UtcNow;
    public void ClearSignaturePayload() { SignatureImage = null; TypedSignatureText = null; TypedSignatureFont = null; }
}
public sealed class ContractEvent { public Guid Id { get; set; }=Guid.NewGuid(); public Guid ContractId { get; set; } public Guid? ActorId { get; set; } public string Type { get; set; }=""; public string Metadata { get; set; }="{}"; public DateTimeOffset OccurredAt { get; set; }=DateTimeOffset.UtcNow; }
public sealed class ContractRetentionPolicy { public Guid Id { get; set; }=Guid.NewGuid(); public int Version { get; set; } public int MinimumYears { get; set; }=5; public bool IsActive { get; set; } public DateTimeOffset CreatedAt { get; set; }=DateTimeOffset.UtcNow; }
