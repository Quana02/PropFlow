using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Infrastructure.Finance;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, Route("api/v1/payments/bank-transfers"), Authorize(Roles = "ACCOUNTANT", Policy = "finance.manage")]
public sealed class BankTransfersController(BankTransferReview review) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet] public Task<PagedBankTransfers> List(string? status = "REVIEW", int page = 1, int pageSize = 20, CancellationToken ct = default) => review.ListAsync(status, page, pageSize, ct);
    [HttpGet("invoices")] public Task<IReadOnlyList<BankTransferInvoiceOption>> Invoices(string? search, CancellationToken ct) => review.CandidatesAsync(search, ct);
    [HttpGet("{id:guid}")] public async Task<ActionResult<BankTransferRow>> Detail(Guid id, CancellationToken ct)
    { try { return Ok(await review.DetailAsync(id, ct)); } catch (PaymentSettingsFailure e) { return Failure(e); } }
    [HttpPost("{id:guid}/assign")] public async Task<IActionResult> Assign(Guid id, AssignBankTransferRequest request, CancellationToken ct)
    { try { await review.AssignAsync(id, request, Actor, ct); return NoContent(); } catch (PaymentSettingsFailure e) { return Failure(e); } }
    [HttpPost("{id:guid}/dismiss")] public async Task<IActionResult> Dismiss(Guid id, DismissBankTransferRequest request, CancellationToken ct)
    { try { await review.DismissAsync(id, request, Actor, ct); return NoContent(); } catch (PaymentSettingsFailure e) { return Failure(e); } }
    private ObjectResult Failure(PaymentSettingsFailure e) => new(new ProblemDetails { Status = e.Status, Title = e.Message, Extensions = { ["code"] = e.Code } }) { StatusCode = e.Status };
}
