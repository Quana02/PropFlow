using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Domain.Tokens;

public class PasswordResetToken
{
    private PasswordResetToken()
    {
        // Parameterless constructor for EF Core
    }

    public PasswordResetToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        string? requestedIp = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        UsedAt = null;
        RequestedIp = requestedIp?.Trim();
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public string? RequestedIp { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int AttemptCount { get; private set; }
    public string? ProofHash { get; private set; }
    public DateTimeOffset? ProofExpiresAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }

    public void RecordFailedAttempt(DateTimeOffset now)
    {
        if (!IsActiveAt(now) || VerifiedAt != null) throw new InvalidOperationException("Recovery challenge is not pending.");
        AttemptCount++;
    }

    public void AuthorizeReset(string proofHash, DateTimeOffset proofExpiresAt, DateTimeOffset now)
    {
        if (!IsActiveAt(now) || VerifiedAt != null) throw new InvalidOperationException("Recovery challenge is not pending.");
        ArgumentException.ThrowIfNullOrWhiteSpace(proofHash);
        if (proofExpiresAt <= now) throw new ArgumentException("Invalid proof expiry.", nameof(proofExpiresAt));
        ProofHash = proofHash;
        ProofExpiresAt = proofExpiresAt;
        VerifiedAt = now;
    }

    public bool CanResetAt(DateTimeOffset now) => !IsUsed && VerifiedAt != null && ProofExpiresAt > now;

    public void ConsumeProof(DateTimeOffset now)
    {
        if (!CanResetAt(now)) throw new InvalidOperationException("Reset proof is not active.");
        UsedAt = now;
    }

    public void Cancel(DateTimeOffset now) => UsedAt ??= now;

    // Within-module navigation
    public UserAccount? User { get; private set; }

    public bool IsUsed => UsedAt != null;

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsActiveAt(DateTimeOffset now) => !IsUsed && !IsExpiredAt(now);

    public void MarkUsed(DateTimeOffset now)
    {
        if (IsUsed)
        {
            throw new InvalidOperationException("Password reset token has already been used.");
        }

        if (IsExpiredAt(now))
        {
            throw new InvalidOperationException("Cannot use an expired password reset token.");
        }

        UsedAt = now;
    }
}
