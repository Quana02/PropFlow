namespace PropFlow.Modules.Payments.Domain.PaymentInstructions;

public sealed class PaymentInstruction
{
    private PaymentInstruction() { }

    public PaymentInstruction(Guid invoiceId, Guid paymentDestinationId, DateTimeOffset now)
    {
        if (invoiceId == Guid.Empty) throw new ArgumentException("Invoice is required.", nameof(invoiceId));
        if (paymentDestinationId == Guid.Empty) throw new ArgumentException("Payment destination is required.", nameof(paymentDestinationId));
        Id = Guid.NewGuid();
        TransferCode = CodeFor(Id);
        InvoiceId = invoiceId;
        PaymentDestinationId = paymentDestinationId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid PaymentDestinationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string TransferCode { get; private set; } = "";
    public static string CodeFor(Guid id) => "PF" + id.ToString("N")[..22].ToUpperInvariant();
}
