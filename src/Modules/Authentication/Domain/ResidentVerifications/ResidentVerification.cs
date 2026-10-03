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

    public ResidentVerification(
        Guid residentId,
        string registrationUsername,
        string registrationDisplayName,
        string registrationEmail,
        string registrationPasswordHash,
        string verificationCodeHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        Guid? apartmentUnitId = null)
    {
        if (residentId == Guid.Empty)
            throw new ArgumentException("ResidentId cannot be empty.", nameof(residentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationPasswordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationCodeHash);
        if (expiresAt <= now)
            throw new ArgumentException("ExpiresAt must be later than now.", nameof(expiresAt));

        Id = Guid.NewGuid();
        ResidentId = residentId;
        ApartmentUnitId = apartmentUnitId;
        RegistrationUsername = registrationUsername.Trim();
        RegistrationDisplayName = registrationDisplayName.Trim();
        RegistrationEmail = registrationEmail.Trim().ToUpperInvariant();
        RegistrationPasswordHash = registrationPasswordHash;
        Status = VerificationStatus.PENDING;
        VerificationCodeHash = verificationCodeHash.Trim();
        ExpiresAt = expiresAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }

    // Registration draft data is kept on the OTP challenge so no UserAccount
    // exists before successful verification. The password hash is cleared as
    // soon as the challenge reaches a terminal state.
    public string? RegistrationUsername { get; private set; }
    public string? RegistrationDisplayName { get; private set; }
    public string? RegistrationEmail { get; private set; }
    public string? RegistrationPasswordHash { get; private set; }

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

    public void CompleteRegistration(Guid userId, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        EnsurePending();
        if (IsExpiredAt(now))
            throw new InvalidOperationException("Cannot verify an expired verification challenge.");
        UserId = userId;
        RegistrationPasswordHash = null;
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
        RegistrationPasswordHash = null;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsurePending();
        Status = VerificationStatus.CANCELLED;
        RegistrationPasswordHash = null;
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
