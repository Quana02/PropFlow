namespace PropFlow.Modules.Payments.Domain.BankTransfers;

// Durable authenticated webhook inbox. The complete payload is encrypted at rest.
public sealed class BankTransfer
{
    private BankTransfer() { }
    public BankTransfer(long transactionId, string payloadHash, string protectedPayload, decimal amount,
        string gateway, string accountLast4, string? transferCode, string? reference, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); TransactionId = transactionId; PayloadHash = payloadHash; ProtectedPayload = protectedPayload;
        Amount = amount; Gateway = gateway; AccountLast4 = accountLast4; TransferCode = transferCode;
        BankReference = reference; ReceivedAt = UpdatedAt = NextAttemptAt = now;
    }
    public Guid Id { get; private set; }
    public long TransactionId { get; private set; }
    public string PayloadHash { get; private set; } = "";
    public string ProtectedPayload { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string Gateway { get; private set; } = "";
    public string AccountLast4 { get; private set; } = "";
    public string? TransferCode { get; private set; }
    public string? BankReference { get; private set; }
    public string Status { get; private set; } = "RECEIVED";
    public string Outcome { get; private set; } = "RECEIVED";
    public Guid? InvoiceId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public decimal AllocatedAmount { get; private set; }
    public Guid? AssignedInvoiceId { get; private set; }
    public decimal? RequestedAmount { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public string? ReviewNote { get; private set; }
    public string ReviewHistory { get; private set; } = "[]";
    public DateTimeOffset? ReviewedAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public int Attempts { get; private set; }
    public int Revision { get; private set; }

    public bool Claim(DateTimeOffset now)
    {
        if (Status is not ("RECEIVED" or "RETRY" or "PROCESSING") || NextAttemptAt > now) return false;
        Status = "PROCESSING"; Attempts++; Touch(now); NextAttemptAt = now.AddMinutes(2); return true;
    }
    public void Complete(string outcome, Guid? invoiceId, Guid? paymentId, decimal allocated, DateTimeOffset now)
    {
        Outcome = outcome; InvoiceId = invoiceId; PaymentId = paymentId; AllocatedAmount = allocated;
        Status = outcome == "CONFIRMED" && allocated >= Amount ? "CONFIRMED" : outcome == "IGNORED_OUTGOING" ? "IGNORED" : "REVIEW";
        if (outcome == "CONFIRMED" && allocated < Amount) Outcome = "EXCESS_REQUIRES_REVIEW";
        Touch(now);
    }
    public void RetryLater(DateTimeOffset now)
    {
        Status = Attempts >= 5 ? "REVIEW" : "RETRY"; Outcome = "PROCESSING_ERROR";
        NextAttemptAt = now.AddSeconds(Math.Min(300, Attempts * 15)); Touch(now);
    }
    public void Assign(Guid invoiceId, decimal amount, Guid actor, string note, DateTimeOffset now)
    {
        if (Status != "REVIEW" || PaymentId.HasValue || invoiceId == Guid.Empty || actor == Guid.Empty ||
            amount <= 0 || amount > Amount || amount != decimal.Round(amount)) throw new InvalidOperationException("Không thể phân bổ giao dịch này.");
        Review(actor, note, now); AssignedInvoiceId = invoiceId; RequestedAmount = amount;
        Status = "RECEIVED"; Outcome = "MANUAL_MATCH_REQUESTED"; Attempts = 0; NextAttemptAt = now; Touch(now);
    }
    public void Dismiss(Guid actor, string note, DateTimeOffset now)
    {
        if (Status != "REVIEW") throw new InvalidOperationException("Giao dịch không còn chờ xử lý.");
        Review(actor, note, now); Status = "DISMISSED"; Outcome = "ACCOUNTANT_RESOLVED"; Touch(now);
    }
    private void Review(Guid actor, string note, DateTimeOffset now)
    {
        if (actor == Guid.Empty || string.IsNullOrWhiteSpace(note) || note.Trim().Length is < 3 or > 1000)
            throw new ArgumentException("Nhập ghi chú xử lý từ 3 đến 1000 ký tự.");
        ReviewedBy = actor; ReviewNote = note.Trim(); ReviewedAt = now;
        var history = System.Text.Json.JsonSerializer.Deserialize<List<ReviewEntry>>(ReviewHistory) ?? [];
        history.Add(new(actor, note.Trim(), now));
        ReviewHistory = System.Text.Json.JsonSerializer.Serialize(history);
    }
    private void Touch(DateTimeOffset now) { UpdatedAt = now; Revision++; }
    private sealed record ReviewEntry(Guid Actor, string Note, DateTimeOffset At);
}
