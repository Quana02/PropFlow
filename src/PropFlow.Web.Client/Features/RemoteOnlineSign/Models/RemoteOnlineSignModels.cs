namespace PropFlow.Web.Client.Features.RemoteOnlineSign.Models;

public sealed record RemoteContractSummary(Guid Id, string ContractCode, string ContractName, Guid CreatorId, Guid? ResidentId, string? ResidentName, string? ResidentCode, string Status, string SigningMode, DateTimeOffset? Deadline, DateTimeOffset MetadataUpdatedAt, int Revision, uint Version, bool ResidentSigned);
public sealed record RemoteDocumentMetadata(Guid Id, string Sha256, long Size, int PageCount, string PageMetadata, bool IsFinal);
public sealed record RemoteSignatureField(Guid Id, string Role, int Page, decimal X, decimal Y, decimal Width, decimal Height);
public sealed record RemoteResidentCandidate(Guid UserId, Guid ResidentId, string FullName, string ResidentCode, IReadOnlyList<string> Apartments);
public sealed record RemoteAuditEvent(Guid Id, Guid? ActorId, string Type, DateTimeOffset OccurredAt, string Metadata);
public sealed record SigningChallenge(Guid SessionId, Guid AuthChallengeId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAfter);
public sealed record CreateRemoteContractRequest(DateTimeOffset? Deadline, string ContractName);
public sealed record SelectRemoteSigningModeRequest(string SigningMode, bool ConfirmFieldReset, uint Version);
public sealed record UpdateRemoteContractMetadataRequest(string ContractName, DateTimeOffset? Deadline, uint Version);
public sealed record RenewDeadlineRequest(DateTimeOffset Deadline, uint Version);
public sealed record AssignResidentRequest(Guid ResidentUserId);
public sealed record SignatureFieldInput(string Role, int Page, decimal X, decimal Y, decimal Width, decimal Height);
public sealed record SendRemoteContractRequest();
public sealed record BeginSignatureRequest(string Method, byte[]? Image, string? Text, string? Font);
public sealed record VerifySignatureRequest(Guid SessionId, string Code);
public sealed record RejectContractRequest(string? Reason);
public sealed record ReassignResidentRequest(Guid ResidentUserId, uint Version);

public static class RemoteContractHistoryLabels
{
    public static string For(string type) => type switch
    {
        "CONTRACT_CREATED" => "Đã tạo hợp đồng",
        "SIGNING_MODE_SELECTED" => "Đã chọn phương thức ký hợp đồng",
        "CONTRACT_METADATA_UPDATED" => "Đã cập nhật thông tin hợp đồng",
        "RESIDENT_ASSIGNED" => "Đã gán cư dân",
        "SIGNATURE_FIELDS_UPDATED" => "Đã cập nhật vùng ký",
        "CONTRACT_SENT_FOR_INVESTOR_SIGNATURE" => "Đã gửi hợp đồng cho chủ đầu tư ký",
        "CONTRACT_SENT_FOR_RESIDENT_SIGNATURE" => "Đã gửi hợp đồng cho cư dân ký",
        "SIGNATURE_OTP_REQUESTED" => "Đã yêu cầu OTP",
        "SIGNATURE_OTP_VERIFIED" => "OTP đã được xác nhận",
        "INVESTOR_SIGNATURE_FINALIZED" => "Chủ đầu tư đã ký",
        "RESIDENT_SIGNATURE_FINALIZED" => "Cư dân đã ký",
        "CONTRACT_COMPLETED" => "Đã hoàn thành hợp đồng",
        "CONTRACT_REJECTED" => "Cư dân đã từ chối ký hợp đồng",
        "RESIDENT_REASSIGNED" => "Đã thay đổi cư dân ký hợp đồng",
        "CONTRACT_RESENT_TO_RESIDENT" => "Đã gửi lại hợp đồng cho cư dân",
        "ORIGINAL_PDF_UPLOADED" => "Đã tải PDF gốc",
        "DOCUMENT_VIEWED" => "Đã xem hợp đồng",
        "CONTRACT_EXPIRED" => "Hợp đồng đã hết hạn",
        "CONTRACT_DEADLINE_RENEWED" => "Đã gia hạn thời hạn ký",
        "CONTRACT_CANCELLED" => "Đã hủy hợp đồng",
        _ => "Hoạt động hợp đồng"
    };
}
