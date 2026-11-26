using System.Security.Claims;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Payments.Application.Finance;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, Route("api/v1/resident/payments"), Authorize(Roles = "RESIDENT")]
public sealed class ResidentPaymentsController(PaymentFinanceUseCases useCases, ResidentServiceRequestPayments payments) : ControllerBase
{
    [HttpGet("invoices/{invoiceId:guid}")]
    public async Task<IActionResult> Instructions(Guid invoiceId, CancellationToken ct)
    {
        try { return Ok(await payments.GetAsync(invoiceId, Guid.Parse(User.FindFirstValue("sub")!), ct, byInvoice: true)); }
        catch (ResidentPaymentFailure ex) { return Problem(statusCode: ex.Status, title: ex.Message); }
    }

    [HttpGet("invoices/{invoiceId:guid}/qr")]
    public async Task<IActionResult> Qr(Guid invoiceId, CancellationToken ct)
    {
        try { return File(await payments.QrAsync(invoiceId, Guid.Parse(User.FindFirstValue("sub")!), ct, byInvoice: true), "image/png"); }
        catch (ResidentPaymentFailure ex) { return Problem(statusCode: ex.Status, title: ex.Message); }
    }

    [HttpPost]
    public async Task<IActionResult> Submit(CollectPaymentRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || request.InvoiceId == Guid.Empty || request.Amount <= 0
            || request.Amount != decimal.Round(request.Amount) || request.PaymentMethodCode != "BANK_TRANSFER"
            || string.IsNullOrWhiteSpace(request.ReferenceNumber) || request.ReferenceNumber.Length > 100 || request.Note?.Length > 1000)
            return Problem(statusCode: 400, title: "Nhập số tiền VND nguyên dương và mã giao dịch chuyển khoản (tối đa 100 ký tự).");
        var result = await useCases.SubmitAsync(request, key, Guid.Parse(User.FindFirstValue("sub")!), ct);
        return result.Outcome switch
        {
            MutationOutcome.Success => Ok(result.Payment),
            MutationOutcome.NotFound => NotFound(),
            MutationOutcome.Overpayment => Problem(statusCode: 409, title: "Số tiền không được vượt quá dư nợ còn lại."),
            MutationOutcome.IdempotencyConflict => Problem(statusCode: 409, title: "Yêu cầu đã được sử dụng. Vui lòng tải lại danh sách."),
            MutationOutcome.DuplicateReference => Conflict(new ProblemDetails
            {
                Status = 409,
                Title = "Mã giao dịch đã được sử dụng.",
                Extensions = { ["code"] = "transfer_reference_exists" }
            }),
            _ => Problem(statusCode: 409, title: "Hóa đơn đã có khoản thanh toán chờ xác minh. Vui lòng chờ kế toán kiểm tra.")
        };
    }
}
