using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Payments.Contracts;
namespace PropFlow.Modules.Billing.Presentation;
[ApiController, Route("api/v1/resident/billing"), Authorize(Roles = "RESIDENT")]
public sealed class ResidentBillingController(IBillingCycle cycle, IPaymentNoticeSource payments) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("invoices")]
    public Task<IReadOnlyList<ResidentInvoice>> Invoices(CancellationToken ct) => cycle.InvoicesAsync(Actor, ct);
    [HttpGet("invoices/{id:guid}/print")]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct) =>
        await cycle.InvoiceDocumentAsync(id, Actor, ct) is { } doc ? Ok(doc) : NotFound();
    [HttpGet("payments")]
    public async Task<IReadOnlyList<PaymentNoticeSnapshot>> Payments(CancellationToken ct) =>
        await payments.ListAsync((await cycle.InvoicesAsync(Actor, ct)).Select(x => x.Id).ToArray(), ct);
    [HttpGet("payments/{id:guid}/receipt")]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct) =>
        await cycle.ReceiptAsync(id, Actor, ct) is { } doc ? Ok(doc) : NotFound();
}
