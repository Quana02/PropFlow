using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Domain.ResidentVerifications;

namespace PropFlow.Modules.Authentication.Application;

public sealed partial class AuthUseCases
{
    // FE-01.1: Registration never creates or modifies residency records.
    public async Task<ChallengeResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        Password(request.Password);
        var normalizedEmail = NormalizeEmail(request.Email);
        var result = await store.SerializedAsync("registration:" + normalizedEmail, async () =>
        {
            if (await store.UsernameAsync(request.Username.Trim(), ct) != null)
                throw new AuthFailure(409, "username_conflict", "Tên đăng nhập đã được sử dụng.");
            if (await store.EmailAsync(normalizedEmail, ct) != null)
                throw new AuthFailure(409, "email_conflict", "Email đã được sử dụng.");
            if (Phone(request.PhoneNumber) is { } phone && await store.PhoneInUseAsync(phone, null, ct))
                throw new AuthFailure(409, "phone_conflict", "Số điện thoại đã được sử dụng.");
            var user = new UserAccount(request.Username, "pending", request.DisplayName, Now, normalizedEmail, Phone(request.PhoneNumber));
            user.ChangePasswordHash(secrets.HashPassword(user, request.Password), null, Now);
            store.Add(user);
            await store.SaveAsync(ct);
            return await CreateRegistrationChallengeAsync(user, ct);
        }, ct);
        await DeliverAsync(result, false, ct);
        return result.Response;
    }

    public async Task<ChallengeResponse> ResumeRegistrationAsync(ResumeRegistrationRequest request, CancellationToken ct)
    {
        var candidate = await store.UsernameAsync(request.Username.Trim(), ct);
        if (candidate == null) { secrets.VerifyPassword(null, request.Password); throw InvalidCredentials(); }
        var result = await store.SerializedAsync("user:" + candidate.Id, async () =>
        {
            var user = (await store.UserAsync(candidate.Id, ct))!;
            var valid = secrets.VerifyPassword(user, request.Password);
            if (!valid || user.Status != AccountStatus.PENDING || user.IsLockedOutAt(Now))
            {
                if (!valid && !user.IsLockedOutAt(Now)) user.RecordLoginFailure(policy.LockoutFailures, TimeSpan.FromMinutes(policy.LockoutMinutes), Now);
                return null;
            }
            return await CreateRegistrationChallengeAsync(user, ct);
        }, ct);
        if (result == null) throw InvalidCredentials();
        await DeliverAsync(result, false, ct);
        return result.Response;
    }

    private sealed record OutgoingChallenge(ChallengeResponse Response, string? Address, string? Code);
    private async Task DeliverAsync(OutgoingChallenge result, bool recovery, CancellationToken ct)
    {
        if (result.Address != null && result.Code != null)
            await email.SendCodeAsync(result.Address, result.Code, recovery, policy.OtpMinutes, ct);
    }
    private async Task<OutgoingChallenge> CreateRegistrationChallengeAsync(UserAccount user, CancellationToken ct)
    {
        var existing = await store.VerificationsAsync(user.Id, ct);
        CheckSendRate(existing.Select(x => x.CreatedAt));
        var eligible = await residents.FindEligibleAsync(user.Email!, Today, ct);
        var expires = Now.AddMinutes(policy.OtpMinutes);
        if (eligible == null)
            return new(new(Guid.Empty, expires, Now.AddSeconds(policy.ResendSeconds), "Tài khoản đang chờ xác minh thông tin cư dân. Vui lòng liên hệ ban quản lý và tiếp tục xác minh sau khi hồ sơ được cập nhật."), null, null);
        foreach (var old in existing.Where(x => x.Status == VerificationStatus.PENDING)) old.Cancel(Now);
        var code = secrets.NewOtp();
        var challenge = new ResidentVerification(user.Id, eligible.Id, secrets.HashOtp(code), expires, Now, eligible.ApartmentUnitId);
        store.Add(challenge);
        return new(new(challenge.Id, expires, Now.AddSeconds(policy.ResendSeconds), "Vui lòng kiểm tra email để xác minh tài khoản cư dân."), user.Email, code);
    }

    // FE-01.3: Eligibility is rechecked at activation, under the account transaction.
    public async Task ActivateAsync(VerifyChallengeRequest request, CancellationToken ct)
    {
        var candidate = await store.VerificationAsync(request.ChallengeId, ct);
        if (candidate == null) throw InvalidChallenge();
        var success = await store.SerializedAsync("user:" + candidate.UserId, async () =>
        {
            var challenge = (await store.VerificationAsync(request.ChallengeId, ct))!;
            var user = (await store.UserAsync(challenge.UserId, ct))!;
            if (challenge.Status != VerificationStatus.PENDING || user.Status != AccountStatus.PENDING) return false;
            if (challenge.IsExpiredAt(Now)) { challenge.Expire(Now); return false; }
            if (challenge.AttemptCount >= policy.OtpAttempts) return false;
            if (!secrets.MatchesOtp(request.Code, challenge.VerificationCodeHash))
            {
                challenge.RecordFailedAttempt(Now);
                if (challenge.AttemptCount >= policy.OtpAttempts) challenge.Cancel(Now);
                return false;
            }
            if (!await residents.LinkEligibleAsync(challenge.ResidentId, user.Id, user.Email!, Today, Now, ct))
                throw new AuthFailure(409, "eligibility_changed", "Không thể liên kết hồ sơ cư dân. Vui lòng liên hệ ban quản lý.");
            await access.GrantResidentAsync(user.Id, Now, ct);
            challenge.Verify(Now);
            user.MarkEmailVerified(Now);
            user.Activate(null, Now);
            return true;
        }, ct);
        if (!success) throw InvalidChallenge();
    }
}
