using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace PropFlow.Modules.Authentication.Contracts;

public sealed record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"[a-zA-Z0-9_.-]+", ErrorMessage = "Tên đăng nhập chỉ gồm chữ, số, dấu chấm, gạch dưới hoặc gạch ngang.")] string Username,
    [Required, StringLength(150)] string DisplayName,
    [Required, StringLength(255)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required, StringLength(20)] string? PhoneNumber,
    [Required] string? IdentityType = null,
    [Required, StringLength(30)] string? IdentityNumber = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(IdentityType) || string.IsNullOrWhiteSpace(IdentityNumber)) yield break;
        var type = IdentityType.Trim().ToUpperInvariant();
        if (type is not ("CCCD" or "CMND"))
        {
            yield return new ValidationResult("Loại giấy tờ chỉ được phép là CCCD hoặc CMND.", [nameof(IdentityType)]);
            yield break;
        }
        if (!MailAddress.TryCreate(Email.Trim(), out _))
            yield return new ValidationResult("Email không hợp lệ.", [nameof(Email)]);
        if (!System.Text.RegularExpressions.Regex.IsMatch(PhoneNumber!.Trim(), @"^(?:\+84|0)\d{9,10}$"))
            yield return new ValidationResult("Số điện thoại không đúng định dạng.", [nameof(PhoneNumber)]);
        if (!System.Text.RegularExpressions.Regex.IsMatch(IdentityNumber.Trim(), @"^[0-9\s./-]+$"))
        {
            yield return new ValidationResult("Số giấy tờ chỉ được gồm chữ số và dấu phân cách.", [nameof(IdentityNumber)]);
            yield break;
        }
        var digits = new string(IdentityNumber.Where(character => character is >= '0' and <= '9').ToArray());
        if (type == "CCCD" && digits.Length != 12)
            yield return new ValidationResult("Số CCCD phải gồm đúng 12 chữ số.", [nameof(IdentityNumber)]);
        if (type == "CMND" && digits.Length is not (9 or 12))
            yield return new ValidationResult("Số CMND phải gồm 9 hoặc 12 chữ số.", [nameof(IdentityNumber)]);
    }
}
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
