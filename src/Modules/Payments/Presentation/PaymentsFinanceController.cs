using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Payments.Application.Finance;
namespace PropFlow.Modules.Payments.Presentation;
[ApiController, Route("api/v1/payments"), Authorize(Roles="ACCOUNTANT", Policy="finance.manage")]
public sealed class PaymentsFinanceController(PaymentFinanceUseCases useCases) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("pending")] public Task<PagedPayments> Pending(string? search, int page=1, int pageSize=20, string sortDirection="desc", CancellationToken ct=default) => useCases.ListAsync("PENDING",search,page,pageSize,sortDirection,ct);
    [HttpGet("confirmed")] public Task<PagedPayments> Confirmed(string? search, int page=1, int pageSize=20, string sortDirection="desc", CancellationToken ct=default) => useCases.ListAsync("CONFIRMED",search,page,pageSize,sortDirection,ct);
    [HttpGet("{id:guid}")] public async Task<ActionResult<PaymentRow>> Detail(Guid id,CancellationToken ct) => await useCases.DetailAsync(id,ct) is {} x ? Ok(x) : Problem(404,"payment_not_found","Không tìm thấy khoản thanh toán.");
    [HttpPost("{id:guid}/confirm")] public async Task<ActionResult<PaymentRow>> Confirm(Guid id,[FromHeader(Name="Idempotency-Key")] string? key,CancellationToken ct) => string.IsNullOrWhiteSpace(key) ? Problem(400,"idempotency_key_required","Thiếu khóa xử lý yêu cầu.") : Map(await useCases.ConfirmAsync(id,key,Actor,ct));
    [HttpPost("{id:guid}/reject")] public async Task<ActionResult<PaymentRow>> Reject(Guid id,RejectRequest request,[FromHeader(Name="Idempotency-Key")] string? key,CancellationToken ct) => string.IsNullOrWhiteSpace(key) ? Problem(400,"idempotency_key_required","Thiếu khóa xử lý yêu cầu.") : string.IsNullOrWhiteSpace(request.Reason) ? Problem(400,"rejection_reason_required","Vui lòng nhập lý do từ chối.") : Map(await useCases.RejectAsync(id,request.Reason,key,Actor,ct));
    private ActionResult<PaymentRow> Map(MutationResult r) => r.Outcome switch { MutationOutcome.Success => Ok(r.Payment), MutationOutcome.NotFound => Problem(404,"payment_not_found","Không tìm thấy khoản thanh toán."), MutationOutcome.Overpayment => Problem(409,"payment_exceeds_outstanding","Số tiền thanh toán vượt quá dư nợ."), MutationOutcome.IdempotencyConflict => Problem(409,"idempotency_conflict","Khóa xử lý đã được sử dụng cho yêu cầu khác."), _ => Problem(409,"payment_already_processed","Khoản thanh toán đã được xử lý.") };
    private ObjectResult Problem(int status,string code,string title) => new(new ProblemDetails{Status=status,Title=title,Extensions={{"code",code},{"traceId",HttpContext.TraceIdentifier}}}){StatusCode=status};
}
public sealed record RejectRequest(string Reason);
