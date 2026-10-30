namespace PropFlow.Modules.Authentication.Domain.PrivilegedActions;

public sealed class PrivilegedActionChallenge
{
    private PrivilegedActionChallenge() { }

    public PrivilegedActionChallenge(
        Guid userId,
        string purpose,
        string payloadHash,
        string codeHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User id is required.", nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        if (expiresAt <= now) throw new ArgumentException("Expiry must be in the future.", nameof(expiresAt));

        Id = Guid.NewGuid();
        UserId = userId;
        Purpose = purpose.Trim().ToUpperInvariant();
        PayloadHash = payloadHash.Trim().ToUpperInvariant();
        CodeHash = codeHash;
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public string CodeHash { get; private set; } = string.Empty;
    public int AttemptCount { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsActiveAt(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void RecordFailedAttempt(DateTimeOffset now, int maximumAttempts)
    {
        if (!IsActiveAt(now)) throw new InvalidOperationException("Challenge is not active.");
        AttemptCount++;
        if (AttemptCount >= maximumAttempts) UsedAt = now;
    }

    public void Consume(DateTimeOffset now)
    {
        if (!IsActiveAt(now)) throw new InvalidOperationException("Challenge is not active.");
        UsedAt = now;
    }

    public void Cancel(DateTimeOffset now) => UsedAt ??= now;
}
