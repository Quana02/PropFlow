namespace PropFlow.Modules.Payments.Domain.PaymentDestinations;

public sealed class PaymentDestination
{
    private PaymentDestination() { }

    public PaymentDestination(
        int version,
        string bankBin,
        string bankCode,
        string bankName,
        string protectedAccountNumber,
        string accountNumberLast4,
        string accountName,
        Guid createdBy,
        DateTimeOffset now,
        Guid? sePayAccountId = null)
    {
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (createdBy == Guid.Empty) throw new ArgumentException("Creator is required.", nameof(createdBy));
        if (sePayAccountId == Guid.Empty) throw new ArgumentException("SePay account id cannot be empty.", nameof(sePayAccountId));
        ArgumentException.ThrowIfNullOrWhiteSpace(bankBin);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(bankName);
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedAccountNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumberLast4);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);

        Id = Guid.NewGuid();
        Version = version;
        BankBin = bankBin;
        BankCode = bankCode;
        BankName = bankName;
        ProtectedAccountNumber = protectedAccountNumber;
        AccountNumberLast4 = accountNumberLast4;
        AccountName = accountName;
        SePayAccountId = sePayAccountId;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedBy = createdBy;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public string BankBin { get; private set; } = string.Empty;
    public string BankCode { get; private set; } = string.Empty;
    public string BankName { get; private set; } = string.Empty;
    public string ProtectedAccountNumber { get; private set; } = string.Empty;
    public string AccountNumberLast4 { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public Guid? SePayAccountId { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public void Deactivate(Guid actorUserId, DateTimeOffset now)
    {
        if (!IsActive) throw new InvalidOperationException("Payment destination is already inactive.");
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorUserId));
        IsActive = false;
        UpdatedBy = actorUserId;
        UpdatedAt = now;
        DeactivatedAt = now;
    }
}
