using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Domain.ResidentVerifications;

public class ResidentVerification
{
    private ResidentVerification()
    {
        // Parameterless constructor for EF Core
    }

    public ResidentVerification(
        Guid userId,
        Guid residentId,
        string verificationCodeHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        Guid? apartmentUnitId = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        if (residentId == Guid.Empty)
        {
            throw new ArgumentException("ResidentId cannot be empty.", nameof(residentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(verificationCodeHash);

        if (expiresAt <= now)
        {
            throw new ArgumentException("ExpiresAt must be later than now.", nameof(expiresAt));
        }

        Id = Guid.NewGuid();
        UserId = userId;
        ResidentId = residentId;
        ApartmentUnitId = apartmentUnitId;
        Status = VerificationStatus.PENDING;
        VerificationCodeHash = verificationCodeHash.Trim();
        AttemptCount = 0;
        ExpiresAt = expiresAt;
        VerifiedAt = null;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    // Cross-module scalar IDs
    public Guid ResidentId { get; private set; }
    public Guid? ApartmentUnitId { get; private set; }

    public VerificationStatus Status { get; private set; } = VerificationStatus.PENDING;
    public string VerificationCodeHash { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Within-module navigation
    public UserAccount? User { get; private set; }

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;

    public void RecordFailedAttempt(DateTimeOffset now)
    {
        EnsurePending();
        if (IsExpiredAt(now))
        {
            throw new InvalidOperationException("Cannot record a failed attempt for an expired verification challenge.");
        }

        AttemptCount++;
        UpdatedAt = now;
    }

    public void Verify(DateTimeOffset now)
    {
        EnsurePending();
        if (IsExpiredAt(now))
        {
            throw new InvalidOperationException("Cannot verify an expired verification challenge.");
        }

        Status = VerificationStatus.VERIFIED;
        VerifiedAt = now;
        UpdatedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        EnsurePending();
        if (!IsExpiredAt(now))
        {
            throw new InvalidOperationException("Cannot expire a verification challenge before its expiry time.");
        }

        Status = VerificationStatus.EXPIRED;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsurePending();
        Status = VerificationStatus.CANCELLED;
        UpdatedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != VerificationStatus.PENDING)
        {
            throw new InvalidOperationException($"Cannot change a verification challenge in status '{Status}'.");
        }
    }
}
