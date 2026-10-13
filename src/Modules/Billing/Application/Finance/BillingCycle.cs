namespace PropFlow.Modules.Billing.Application.Finance;

public sealed record ServiceRateInput(decimal UnitRate, DateOnly EffectiveFrom);
public sealed record ServiceRateView(Guid Id, decimal UnitRate, DateOnly EffectiveFrom, Guid CreatedBy);
public sealed record ResidentInvoice(Guid Id, string Number, string Apartment, string ResidentName,
    DateOnly Period, DateOnly? DueDate, decimal Total, decimal Paid, decimal Outstanding,
    decimal ServiceTotal = 0, decimal RentalTotal = 0, string Capacity = "HOUSEHOLD_HEAD", string? InvoiceType = null);
public sealed record FinanceDocument(byte[] Content, string ContentType, string FileName);
public interface IBillingCycle
{
    Task<IReadOnlyList<ServiceRateView>> RatesAsync(CancellationToken ct);
    Task SaveRateAsync(ServiceRateInput input, Guid actor, CancellationToken ct);
    Task RunAsync(CancellationToken ct);
    Task NotifyPaymentAsync(Guid invoiceId, CancellationToken ct);
    Task<IReadOnlyList<ResidentInvoice>> InvoicesAsync(Guid? userId, CancellationToken ct);
    Task<FinanceDocument?> InvoiceDocumentAsync(Guid id, Guid? userId, CancellationToken ct);
    Task<FinanceDocument?> ReceiptAsync(Guid paymentId, Guid? userId, CancellationToken ct);
    Task<int> RemindAsync(IReadOnlyList<Guid> ids, CancellationToken ct);
}
