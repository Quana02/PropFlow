using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Domain.Tokens;

namespace PropFlow.Modules.Authentication.Application;

public sealed partial class AuthUseCases
{
    // FE-01.4. Always return an opaque challenge, including unknown/ineligible emails.
    public async Task<ChallengeResponse> ForgotAsync(ForgotPasswordRequest request, string? ip, CancellationToken ct)
    {
        var message = "Nếu tài khoản có thể khôi phục qua email này, bạn sẽ nhận được mã xác minh. Nếu không nhận được mã, vui lòng liên hệ hỗ trợ.";
        var dummy = new ChallengeResponse(Guid.NewGuid(), Now.AddMinutes(policy.OtpMinutes), Now.AddSeconds(policy.ResendSeconds), message);
        var candidate = await store.EmailAsync(NormalizeEmail(request.Email), ct);
        if (candidate == null) return dummy;
        var result = await store.SerializedAsync("user:" + candidate.Id, async () =>
        {
            var user = (await store.UserAsync(candidate.Id, ct))!;
            if (!user.EmailVerified || user.Status != AccountStatus.ACTIVE) return null;
            var old = await store.RecoveriesAsync(user.Id, ct);
            // Suppress account-specific rate information to avoid an existence oracle.
            if (old.Count(x => x.CreatedAt > Now.AddHours(-1)) >= policy.SendsPerHour || old.Any(x => x.CreatedAt > Now.AddSeconds(-policy.ResendSeconds))) return null;
            foreach (var previous in old) previous.Cancel(Now);
            var code = secrets.NewOtp();
            var challenge = new PasswordResetToken(user.Id, secrets.HashOtp(code), Now.AddMinutes(policy.OtpMinutes), Now, ip);
            store.Add(challenge);
            return new OutgoingChallenge(new(challenge.Id, challenge.ExpiresAt, Now.AddSeconds(policy.ResendSeconds), message), user.Email, code);
        }, ct);
        if (result == null) return dummy;
        await DeliverAsync(result, true, ct);
        return result.Response;
    }

    public async Task<ResetProofResponse> VerifyRecoveryAsync(VerifyChallengeRequest request, CancellationToken ct)
    {
        var candidate = await store.RecoveryAsync(request.ChallengeId, ct);
        if (candidate == null) throw InvalidChallenge();
        var result = await store.SerializedAsync("user:" + candidate.UserId, async () =>
        {
            var challenge = (await store.RecoveryAsync(request.ChallengeId, ct))!;
            if (!challenge.IsActiveAt(Now) || challenge.VerifiedAt != null || challenge.AttemptCount >= policy.OtpAttempts) return null;
            if (!secrets.MatchesOtp(request.Code, challenge.TokenHash))
            {
                challenge.RecordFailedAttempt(Now);
                if (challenge.AttemptCount >= policy.OtpAttempts) challenge.Cancel(Now);
                return null;
            }
            var raw = secrets.NewToken();
            var expiry = Now.AddMinutes(policy.OtpMinutes);
            challenge.AuthorizeReset(secrets.HashToken(raw), expiry, Now);
            return new ResetProofResponse(challenge.Id, raw, expiry);
        }, ct);
        return result ?? throw InvalidChallenge();
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        Password(request.NewPassword);
        var candidate = await store.RecoveryAsync(request.ChallengeId, ct);
        if (candidate == null) throw InvalidChallenge();
        await store.SerializedAsync("user:" + candidate.UserId, async () =>
        {
            var challenge = (await store.RecoveryAsync(request.ChallengeId, ct))!;
            if (!challenge.CanResetAt(Now) || challenge.ProofHash != secrets.HashToken(request.Proof)) throw InvalidChallenge();
            var user = await ActiveUserAsync(challenge.UserId, ct);
            user.ChangePasswordHash(secrets.HashPassword(user, request.NewPassword), user.Id, Now);
            challenge.ConsumeProof(Now);
            foreach (var reset in await store.RecoveriesAsync(user.Id, ct)) reset.Cancel(Now);
            await RevokeAllAsync(user.Id, "password_reset", ct);
            return true;
        }, ct);
    }
}
