using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.PrivilegedActions;
using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Application.PrivilegedActions;

public sealed class PrivilegedActionOtp(
    IAuthStore store,
    IAuthSecrets secrets,
    IAuthEmail email,
    AuthPolicy policy,
    TimeProvider clock) : IPrivilegedActionOtp
{
    public async Task<PrivilegedActionChallengeResult> RequestAsync(
        Guid userId,
        string purpose,
        string payloadHash,
        CancellationToken ct)
    {
        var normalizedPurpose = NormalizePurpose(purpose);
        var normalizedHash = NormalizeHash(payloadHash);
        var now = clock.GetUtcNow();
        var outgoing = await store.SerializedAsync("privileged-action:" + userId, async () =>
        {
            var user = await store.UserAsync(userId, ct);
            if (user is null || user.Status != AccountStatus.ACTIVE || !user.EmailVerified || string.IsNullOrWhiteSpace(user.Email))
                throw new AuthFailure(409, "verified_email_required", "Tài khoản Admin cần email đã xác minh để thực hiện thao tác này.");

            var previous = await store.PrivilegedChallengesAsync(userId, normalizedPurpose, ct);
            var recent = previous.Where(item => item.CreatedAt > now.AddHours(-1)).ToArray();
            if (recent.Length >= policy.SendsPerHour || recent.Any(item => item.CreatedAt > now.AddSeconds(-policy.ResendSeconds)))
                throw new AuthFailure(429, "resend_limited", "Vui lòng chờ trước khi yêu cầu mã mới hoặc thử lại sau.");
            foreach (var active in previous.Where(item => item.IsActiveAt(now))) active.Cancel(now);

            var code = secrets.NewOtp();
            var binding = Binding(userId, normalizedPurpose, normalizedHash);
            var challenge = new PrivilegedActionChallenge(userId, normalizedPurpose, normalizedHash,
                secrets.BindOtp(code, binding), now.AddMinutes(policy.OtpMinutes), now);
            store.Add(challenge);
            return (challenge, user.Email, code);
        }, ct);

        await email.SendPrivilegedActionCodeAsync(outgoing.Email!, outgoing.code, policy.OtpMinutes, ct);
        return new(outgoing.challenge.Id, outgoing.challenge.ExpiresAt, now.AddSeconds(policy.ResendSeconds), MaskEmail(outgoing.Email!));
    }

    public async Task VerifyAsync(
        Guid userId,
        Guid challengeId,
        string purpose,
        string payloadHash,
        string code,
        CancellationToken ct)
    {
        var normalizedPurpose = NormalizePurpose(purpose);
        var normalizedHash = NormalizeHash(payloadHash);
        var now = clock.GetUtcNow();
        var success = await store.SerializedAsync("privileged-action:" + userId, async () =>
        {
            var challenge = await store.PrivilegedChallengeAsync(challengeId, ct);
            if (challenge is null || challenge.UserId != userId || challenge.Purpose != normalizedPurpose ||
                challenge.PayloadHash != normalizedHash || !challenge.IsActiveAt(now) || challenge.AttemptCount >= policy.OtpAttempts)
                return false;

            var binding = Binding(userId, normalizedPurpose, normalizedHash);
            if (!secrets.MatchesBoundOtp(code, challenge.CodeHash, binding))
            {
                challenge.RecordFailedAttempt(now, policy.OtpAttempts);
                return false;
            }

            challenge.Consume(now);
            return true;
        }, ct);
        if (!success) throw new AuthFailure(400, "invalid_challenge", "Mã xác minh không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
    }

    private static string NormalizePurpose(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 80) throw new ArgumentException("Purpose is too long.", nameof(value));
        return normalized;
    }

    private static string NormalizeHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Payload hash must be SHA-256 hexadecimal.", nameof(value));
        return normalized;
    }

    private static string Binding(Guid userId, string purpose, string payloadHash) => $"{userId:N}|{purpose}|{payloadHash}";

    private static string MaskEmail(string email)
    {
        var separator = email.IndexOf('@');
        if (separator <= 1) return "***" + email[Math.Max(0, separator)..];
        return email[0] + new string('*', Math.Min(6, separator - 1)) + email[(separator - 1)..];
    }
}
