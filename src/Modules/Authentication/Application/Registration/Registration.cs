using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Domain.ResidentVerifications;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Modules.Authentication.Application;

public sealed partial class AuthUseCases
{
    private const string RegistrationMismatch = "Thông tin cư dân không khớp hoặc chưa đủ điều kiện đăng ký tài khoản.";

    // FE-01.1: Registration never creates or modifies residency records.
    public async Task<ChallengeResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        Password(request.Password);
        var normalizedEmail = ResidentRegistrationNormalization.Email(request.Email);
        var accountEmail = NormalizeEmail(request.Email);
        var normalizedPhone = ResidentRegistrationNormalization.Phone(request.PhoneNumber!);
        var identityType = ResidentRegistrationNormalization.IdentityType(request.IdentityType!);
        var normalizedIdentityNumber = ResidentRegistrationNormalization.IdentityNumber(request.IdentityNumber!);
        var candidate = await residents.FindRegistrationCandidateAsync(
            normalizedEmail, normalizedPhone, identityType, normalizedIdentityNumber, Today, ct);
        if (candidate is null)
            return new(Guid.Empty, Now.AddMinutes(policy.OtpMinutes), Now.AddSeconds(policy.ResendSeconds), RegistrationMismatch);
        var username = request.Username.Trim();
        var result = await store.SerializedAsync("registration", async () =>
        {
            if (await store.UsernameAsync(username, ct) != null)
                throw new AuthFailure(409, "username_conflict", "Tên đăng nhập đã được sử dụng.");
            if (await store.EmailAsync(accountEmail, ct) != null)
                throw new AuthFailure(409, "email_conflict", "Email đã được sử dụng.");
            var previous = await store.RegistrationAttemptsAsync(username, accountEmail, ct);
            foreach (var expired in previous.Where(x => x.Status == VerificationStatus.PENDING && x.IsExpiredAt(Now)))
                expired.Expire(Now);
            if (previous.Any(x => x.Status == VerificationStatus.PENDING && x.RegistrationUsername == username))
                throw new AuthFailure(409, "username_conflict", "Tên đăng nhập đã được sử dụng.");
            if (previous.Any(x => x.Status == VerificationStatus.PENDING && x.RegistrationEmail == accountEmail))
                throw new AuthFailure(409, "email_conflict", "Email đã được sử dụng.");
            var passwordHash = secrets.HashRegistrationPassword(username, request.Password);
            return CreateRegistrationChallenge(username, request.DisplayName, accountEmail, passwordHash, candidate, previous);
        }, ct);
        await DeliverAsync(result, false, ct);
        return result.Response;
    }

    public async Task<ChallengeResponse> ResumeRegistrationAsync(ResumeRegistrationRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var initial = await store.PendingRegistrationAsync(username, ct);
        if (initial == null) { secrets.VerifyRegistrationPassword(username, null, request.Password); throw InvalidCredentials(); }
        var result = await store.SerializedAsync("registration", async () =>
        {
            var current = await store.PendingRegistrationAsync(username, ct);
            if (current == null || current.IsExpiredAt(Now)
                || !secrets.VerifyRegistrationPassword(username, current.RegistrationPasswordHash, request.Password)) return null;
            var candidate = await residents.RevalidateRegistrationCandidateAsync(
                current.ResidentId, Today, ct);
            if (candidate is null) return null;
            var existing = await store.RegistrationAttemptsAsync(username, current.RegistrationEmail!, ct);
            var next = CreateRegistrationChallenge(username, current.RegistrationDisplayName!, current.RegistrationEmail!,
                current.RegistrationPasswordHash!, candidate, existing);
            current.Cancel(Now);
            return next;
        }, ct);
        if (result == null) throw InvalidCredentials();
        await DeliverAsync(result, false, ct);
        return result.Response;
    }

    private sealed record OutgoingChallenge(ChallengeResponse Response, string? Address, string? Code);
    private async Task DeliverAsync(OutgoingChallenge result, bool recovery, CancellationToken ct)
    {
        if (result.Address != null && result.Code != null)
        {
            try
            {
                await email.SendCodeAsync(result.Address, result.Code, recovery, policy.OtpMinutes, ct);
            }
            catch when (!recovery)
            {
                // Delivery happens after commit. Release this attempt even if the request was cancelled.
                await CancelRegistrationAsync(result.Response.ChallengeId);
                throw;
            }
        }
    }

    private Task<bool> CancelRegistrationAsync(Guid challengeId) => store.SerializedAsync("registration", async () =>
    {
        var challenge = await store.VerificationAsync(challengeId, CancellationToken.None);
        if (challenge is { Status: VerificationStatus.PENDING, RegistrationUsername: not null })
            challenge.Cancel(Now);
        return true;
    }, CancellationToken.None);
    private OutgoingChallenge CreateRegistrationChallenge(
        string username,
        string displayName,
        string accountEmail,
        string passwordHash,
        RegistrationResidentCandidate eligible,
        IReadOnlyCollection<ResidentVerification> existing)
    {
        CheckSendRate(existing.Select(x => x.CreatedAt));
        var expires = Now.AddMinutes(policy.OtpMinutes);
        var code = secrets.NewOtp();
        var challenge = new ResidentVerification(eligible.Id, username, displayName, accountEmail, passwordHash,
            secrets.BindOtp(code, CandidateBinding(eligible)), expires, Now, eligible.ApartmentUnitId);
        store.Add(challenge);
        return new(new(challenge.Id, expires, Now.AddSeconds(policy.ResendSeconds), "Vui lòng kiểm tra email để xác minh tài khoản cư dân."), accountEmail, code);
    }

    // FE-01.3: Eligibility is rechecked at activation, under the account transaction.
    public async Task ActivateAsync(VerifyChallengeRequest request, CancellationToken ct)
    {
        var candidate = await store.VerificationAsync(request.ChallengeId, ct);
        if (candidate == null) throw InvalidChallenge();
        bool success;
        try
        {
            success = await store.SerializedAsync("registration", async () =>
            {
                var challenge = (await store.VerificationAsync(request.ChallengeId, ct))!;
                if (challenge.Status != VerificationStatus.PENDING || challenge.RegistrationUsername == null
                    || challenge.RegistrationDisplayName == null || challenge.RegistrationEmail == null
                    || challenge.RegistrationPasswordHash == null) return false;
                if (challenge.IsExpiredAt(Now)) { challenge.Expire(Now); return false; }
                if (challenge.AttemptCount >= policy.OtpAttempts) return false;
                var eligible = await residents.RevalidateRegistrationCandidateAsync(challenge.ResidentId, Today, ct);
                if (eligible is null || !secrets.IsOtpBoundTo(challenge.VerificationCodeHash, CandidateBinding(eligible)))
                    throw new AuthFailure(409, "eligibility_changed", RegistrationMismatch);
                if (!secrets.MatchesBoundOtp(request.Code, challenge.VerificationCodeHash, CandidateBinding(eligible)))
                {
                    challenge.RecordFailedAttempt(Now);
                    if (challenge.AttemptCount >= policy.OtpAttempts) challenge.Cancel(Now);
                    return false;
                }
                if (await store.UsernameAsync(challenge.RegistrationUsername, ct) != null)
                    throw new AuthFailure(409, "username_conflict", "Tên đăng nhập đã được sử dụng.");
                if (await store.EmailAsync(challenge.RegistrationEmail, ct) != null)
                    throw new AuthFailure(409, "email_conflict", "Email đã được sử dụng.");
                var user = new UserAccount(challenge.RegistrationUsername, challenge.RegistrationPasswordHash,
                    challenge.RegistrationDisplayName, Now, challenge.RegistrationEmail);
                store.Add(user);
                await store.SaveAsync(ct);
                if (!await residents.LinkEligibleAsync(eligible, user.Id, Today, Now, ct))
                    throw new AuthFailure(409, "eligibility_changed", RegistrationMismatch);
                await access.GrantResidentAsync(user.Id, Now, ct);
                challenge.CompleteRegistration(user.Id, Now);
                user.MarkEmailVerified(Now);
                user.Activate(null, Now);
                return true;
            }, ct);
        }
        catch (AuthFailure failure) when (failure.Code == "eligibility_changed")
        {
            // The account transaction rolled back; persist cancellation separately so retry can use the email.
            await CancelRegistrationAsync(request.ChallengeId);
            throw;
        }
        if (!success) throw InvalidChallenge();
    }
}
