namespace PropFlow.Modules.Billing.Application.Finance;

public sealed record ServiceRateInput(decimal UnitRate, DateOnly EffectiveFrom, decimal? WaterUnitRate = null, decimal? ElectricityUnitRate = null, decimal? WaterM3UnitRate = null);
public sealed record ServiceRateView(Guid Id, decimal UnitRate, DateOnly EffectiveFrom, Guid CreatedBy, decimal? WaterUnitRate = null, decimal? ElectricityUnitRate = null, decimal? WaterM3UnitRate = null);
public sealed record ElectricityUsageInput(DateOnly UsageMonth, decimal Kwh);
public sealed record UtilityUsageInput(DateOnly UsageMonth, decimal? Kwh, decimal? WaterM3);
public sealed record ElectricityUsageView(Guid ApartmentId, DateOnly UsageMonth, decimal? Kwh, bool IsLocked, DateTimeOffset? UpdatedAt, decimal? WaterM3 = null);
public sealed record ResidentInvoice(Guid Id, string Number, string Apartment, string ResidentName,
    DateOnly Period, DateOnly? DueDate, decimal Total, decimal Paid, decimal Outstanding,
    decimal ServiceTotal = 0, decimal RentalTotal = 0, string Capacity = "HOUSEHOLD_HEAD", string? InvoiceType = null);
public sealed record FinanceDocument(byte[] Content, string ContentType, string FileName);
public sealed record InvoiceDetailLine(Guid Id, Guid? FeeTypeId, Guid? RateId, string FeeCode, string FeeName,
    string CalculationMethod, decimal Quantity, string? UnitName, decimal UnitRate, decimal Amount, string? Description, int AmountDivisor);
public sealed record InvoiceDetailHistory(string? FromStatus, string ToStatus, Guid ChangedBy, DateTimeOffset ChangedAt, string? Reason);
public sealed record InvoiceDetail(Guid Id, string InvoiceNumber, Guid ApartmentUnitId, string ApartmentNumber,
    DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly? IssueDate, DateOnly? DueDate, string Status,
    decimal TotalAmount, decimal ConfirmedPaidAmount, decimal OutstandingAmount, string SettlementStatus,
    DateTimeOffset CreatedAt, Guid CreatedBy, DateTimeOffset? IssuedAt, Guid? IssuedBy, string? Note,
    IReadOnlyList<InvoiceDetailLine> Lines, IReadOnlyList<InvoiceDetailHistory> History, string? ResidentName, string? InvoiceType);
public sealed record ServiceInvoiceCorrection(string Key, decimal ServiceUnitRate, decimal WaterUnitRate,
    decimal ElectricityUnitRate, decimal WaterM3, decimal Kwh, string Reason);

public interface IBillingCycle
{
    Task SaveUtilitiesAsync(Guid apartmentId, UtilityUsageInput input, Guid actor, CancellationToken ct);
    Task<ElectricityUsageView> ElectricityAsync(Guid apartmentId, DateOnly month, CancellationToken ct);
    Task SaveElectricityAsync(Guid apartmentId, ElectricityUsageInput input, Guid actor, CancellationToken ct);
    Task<InvoiceDetail?> DetailAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ServiceRateView>> RatesAsync(CancellationToken ct);
    Task SaveRateAsync(ServiceRateInput input, Guid actor, CancellationToken ct);
    Task RunAsync(CancellationToken ct);
    Task NotifyPaymentAsync(Guid invoiceId, CancellationToken ct);
    Task<IReadOnlyList<ResidentInvoice>> InvoicesAsync(Guid? userId, CancellationToken ct);
    Task<FinanceDocument?> InvoiceDocumentAsync(Guid id, Guid? userId, CancellationToken ct);
    Task<FinanceDocument?> ReceiptAsync(Guid paymentId, Guid? userId, CancellationToken ct);
    Task<int> RemindAsync(IReadOnlyList<Guid> ids, CancellationToken ct, bool overdueOnly = false, Guid? requestId = null);
    Task<int> SendInvoiceAsync(Guid id, CancellationToken ct, Guid? requestId = null);
    Task CorrectUnpaidServiceInvoiceAsync(Guid id, ServiceInvoiceCorrection input, Guid actor, CancellationToken ct);
}
