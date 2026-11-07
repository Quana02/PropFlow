using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Payments.Application.Finance;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, Route("api/v1/resident/payments"), Authorize(Roles = "RESIDENT")]
public sealed class ResidentPaymentsController(PaymentFinanceUseCases useCases) : ControllerBase
{
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
            _ => Problem(statusCode: 409, title: "Hóa đơn đã có khoản thanh toán chờ xác minh. Vui lòng chờ kế toán kiểm tra.")
        };
    }
}
