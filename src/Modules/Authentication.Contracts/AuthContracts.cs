using System.ComponentModel.DataAnnotations;

namespace PropFlow.Modules.Authentication.Contracts;

public sealed record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"[a-zA-Z0-9_.-]+", ErrorMessage = "Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.")] string Username,
    [Required, StringLength(150)] string DisplayName,
    [Required, EmailAddress, StringLength(255)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [StringLength(20)] string? PhoneNumber);
public sealed record LoginRequest([Required, StringLength(50)] string Username, [Required, StringLength(128)] string Password);
public sealed record ResumeRegistrationRequest([Required, StringLength(50)] string Username, [Required, StringLength(128)] string Password);
public sealed record VerifyChallengeRequest(Guid ChallengeId, [Required, RegularExpression(@"\d{6}")] string Code);
public sealed record ForgotPasswordRequest([Required, EmailAddress, StringLength(255)] string Email);
public sealed record ResetPasswordRequest(Guid ChallengeId, [Required, StringLength(200)] string Proof, [Required, StringLength(128, MinimumLength = 12)] string NewPassword);
public sealed record ChangePasswordRequest([Required, StringLength(128)] string CurrentPassword, [Required, StringLength(128, MinimumLength = 12)] string NewPassword);
public sealed record UpdateProfileRequest([Required, StringLength(150)] string DisplayName, [StringLength(20)] string? PhoneNumber);
public sealed record ChallengeResponse(Guid ChallengeId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAfter, string Message);
public sealed record ResetProofResponse(Guid ChallengeId, string Proof, DateTimeOffset ExpiresAt);
public sealed record AccountResponse(Guid Id, string Username, string DisplayName, string? Email, string? PhoneNumber, string Status, bool EmailVerified, string Role, string[] Permissions);
public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, AccountResponse User);
public sealed record CsrfResponse(string Token);

// Public Administration-facing identity boundary.  Account credentials and
// status remain owned by Authentication; Administration owns role assignment.
public sealed record InternalAccountRecord(Guid Id, string Username, string DisplayName, string? Email, string Status);
public sealed record PagedInternalAccountRecords(int Total, IReadOnlyList<InternalAccountRecord> Items);
public sealed record CreateInternalAccount(string Username, string DisplayName, string Email, string Password, string? PhoneNumber);
public sealed class InternalAccountConflictException(string message) : Exception(message);
public interface IInternalAccountDirectory
{
    Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
    Task<PagedInternalAccountRecords> SearchAsync(IReadOnlyCollection<Guid> userIds, string? search, string? status, int page, int pageSize, CancellationToken ct);
    Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct);
    Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct);
}
