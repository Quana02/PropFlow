namespace PropFlow.Modules.Billing.Domain.Invoices;

// Captured at issuance so a later household change cannot expose old invoices.
public sealed class InvoiceRecipient
{
    private InvoiceRecipient() { }
    public InvoiceRecipient(Guid invoiceId, Guid? userId, string name, string? email, string unitNumber, decimal areaM2, Guid? residentId = null, string capacity = "HOUSEHOLD_HEAD")
    { Id = Guid.NewGuid(); InvoiceId = invoiceId; UserId = userId == Guid.Empty ? null : userId; FullName = name; Email = email; UnitNumber = unitNumber; AreaM2 = areaM2; ResidentId = residentId; Capacity = capacity; }
    public Guid Id { get; private set; }
    public Guid? ResidentId { get; private set; }
    public string Capacity { get; private set; } = "HOUSEHOLD_HEAD";
    public Guid InvoiceId { get; private set; }
    public Guid? UserId { get; private set; }
    public string FullName { get; private set; } = "";
    public string? Email { get; private set; }
    public string UnitNumber { get; private set; } = "";
    public decimal AreaM2 { get; private set; }
    public void LinkAccount(Guid userId)
    {
        if (userId == Guid.Empty || ResidentId is null) throw new ArgumentException("A known resident and account are required.");
        if (UserId is not null && UserId != userId) throw new InvalidOperationException("An addressed account cannot be reassigned.");
        UserId = userId;
    }
}
