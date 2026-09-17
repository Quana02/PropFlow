using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Domain.Tokens;
using PropFlow.Modules.Authentication.Domain.ResidentVerifications;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Authentication.Application;

// FE-01 shared orchestration dependencies; implementations are grouped by user goal.
public sealed partial class AuthUseCases(IAuthStore store, IAuthSecrets secrets, IAuthEmail email,
    IResidentOnboarding residents, IAccountAccess access, AuthPolicy policy, TimeProvider clock)
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")).DateTime);
    private static string NormalizeEmail(string value) => value.Trim().ToUpperInvariant();
    private static string? Phone(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AuthFailure InvalidCredentials() => new(401, "invalid_credentials", "Không thể đăng nhập bằng thông tin đã cung cấp. Vui lòng thử lại sau hoặc liên hệ hỗ trợ.");
    private static AuthFailure InvalidChallenge() => new(400, "invalid_challenge", "Mã xác minh không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
    private static void Password(string value)
    {
        if (value.Length is < 12 or > 128) throw new AuthFailure(400, "invalid_password", "Mật khẩu phải có từ 12 đến 128 ký tự.");
    }
    private async Task<AccountResponse> AccountAsync(UserAccount user, CancellationToken ct)
    {
        var grants = await access.GetAsync(user.Id, ct);
        if (grants == null) throw new AuthFailure(403, "access_unavailable", "Tài khoản chưa được cấp quyền truy cập. Vui lòng liên hệ quản trị viên.");
        return new(user.Id, user.Username, user.DisplayName, user.Email, user.PhoneNumber, user.Status.ToString(), user.EmailVerified, grants.Role, grants.Permissions);
    }
    private async Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct)
    {
        foreach (var session in await store.SessionsAsync(userId, ct)) session.Revoke(reason, Now);
    }
    private void CheckSendRate(IEnumerable<DateTimeOffset> dates)
    {
        var recent = dates.Where(x => x > Now.AddHours(-1)).ToArray();
        if (recent.Length >= policy.SendsPerHour || recent.Any(x => x > Now.AddSeconds(-policy.ResendSeconds)))
            throw new AuthFailure(429, "resend_limited", "Vui lòng chờ trước khi yêu cầu mã mới hoặc thử lại sau.");
    }
}
