using System.ComponentModel.DataAnnotations;
namespace PropFlow.Web.Client.Features.Finance.Models;

public sealed record FeeInput([Required, StringLength(50)] string Code, [Required, StringLength(150)] string Name,
    [Required, StringLength(50)] string CalculationMethod, [Required, StringLength(50)] string BillingFrequency, string? Description);
public sealed record RateInput(Guid FeeTypeId, [Required, StringLength(150)] string Name,
    decimal UnitRate, [StringLength(50)] string? UnitName, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record FeeView(Guid Id, string Code, string Name, string CalculationMethod, string BillingFrequency, string? Description, string Status);
public sealed record RateView(Guid Id, Guid FeeTypeId, string FeeName, string Name, string CalculationMethod,
    decimal UnitRate, string? UnitName, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);
public sealed record DraftLine(Guid RateId, decimal Quantity, string? Description);
public sealed record DraftInput([Required, StringLength(50)] string InvoiceNumber, Guid ApartmentUnitId,
    DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly? DueDate, string? Note, [Required, MinLength(1)] DraftLine[] Lines);
public sealed record IssueInput(DateOnly IssueDate, DateOnly? DueDate);
public sealed record CancelInput([Required] string Reason);
public sealed record InvoiceFilter(string? Search = null, string? Status = null, Guid? ApartmentId = null,
    DateOnly? From = null, DateOnly? To = null, string? Settlement = null);
public sealed record InvoiceLineView(Guid Id, Guid? FeeTypeId, Guid? RateId, string FeeCode, string FeeName,
    string CalculationMethod, decimal Quantity, string? UnitName, decimal UnitRate, decimal Amount, string? Description, int AmountDivisor = 1);
public sealed record InvoiceHistoryView(string? FromStatus, string ToStatus, Guid ChangedBy, DateTimeOffset ChangedAt, string? Reason);
public sealed record InvoiceView(Guid Id, string InvoiceNumber, Guid ApartmentUnitId, string ApartmentNumber,
    DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly? IssueDate, DateOnly? DueDate, string Status,
    decimal TotalAmount, decimal ConfirmedPaidAmount, decimal OutstandingAmount, string SettlementStatus,
    DateTimeOffset CreatedAt, Guid CreatedBy, DateTimeOffset? IssuedAt, Guid? IssuedBy, string? Note,
    IReadOnlyList<InvoiceLineView> Lines, IReadOnlyList<InvoiceHistoryView> History, string? ResidentName = null, string? InvoiceType = null);
public sealed record InvoicePage(int Total, int Page, int PageSize, decimal TotalAmount, decimal ConfirmedPaidAmount,
    decimal OutstandingAmount, IReadOnlyList<InvoiceView> Items);
public sealed record BillingApartment(Guid Id, string UnitNumber);

public sealed record SavedId(Guid Id);
public sealed record FileResponse(byte[] Content, string ContentType, string FileName);
