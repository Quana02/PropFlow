using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Billing.Application.Finance;
namespace PropFlow.Modules.Billing.Presentation;
[ApiController, Route("api/v1/billing/cycle"), Authorize]
public sealed class BillingCycleController(IBillingCycle cycle) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("rates"), Authorize(Policy = "billing.read")]
    public Task<IReadOnlyList<ServiceRateView>> Rates(CancellationToken ct) => cycle.RatesAsync(ct);
    [HttpPost("rates"), Authorize(Policy = "billing.configure")]
    public async Task<IActionResult> Save(ServiceRateInput input, CancellationToken ct)
    {
        try { await cycle.SaveRateAsync(input, Actor, ct); await cycle.RunAsync(ct); return Ok(new { saved = true }); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message }); }
    }
    [HttpGet("invoices"), Authorize(Policy = "billing.read")]
    public Task<IReadOnlyList<ResidentInvoice>> Invoices(CancellationToken ct) => cycle.InvoicesAsync(null, ct);
    [HttpGet("invoices/{id:guid}/print"), Authorize(Policy = "billing.read")]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct) =>
        await cycle.InvoiceDocumentAsync(id, null, ct) is { } doc ? Ok(doc) : NotFound();
    [HttpGet("payments/{id:guid}/receipt"), Authorize(Policy = "finance.manage")]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct) =>
        await cycle.ReceiptAsync(id, null, ct) is { } doc ? Ok(doc) : NotFound();
    [HttpPost("reminders"), Authorize(Policy = "finance.manage")]
    public async Task<IActionResult> Remind(ReminderRequest request, CancellationToken ct)
    {
        if (request.InvoiceIds is null || request.InvoiceIds.Length > 100) return BadRequest();
        return Ok(new { recipients = await cycle.RemindAsync(request.InvoiceIds.Distinct().ToArray(), ct) });
    }
}
public sealed record ReminderRequest(Guid[] InvoiceIds);
