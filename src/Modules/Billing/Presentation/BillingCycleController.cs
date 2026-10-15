using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Billing.Application.Finance;
namespace PropFlow.Modules.Billing.Presentation;
[ApiController, Route("api/v1/billing/cycle"), Authorize]
public sealed class BillingCycleController(IBillingCycle cycle) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("apartments/{id:guid}/electricity"), Authorize(Roles = "MANAGER", Policy = "billing.configure")]
    public async Task<IActionResult> Electricity(Guid id, DateOnly usageMonth, CancellationToken ct)
    {
        try { return Ok(await cycle.ElectricityAsync(id, usageMonth, ct)); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message }); }
    }
    [HttpPut("apartments/{id:guid}/electricity"), Authorize(Roles = "MANAGER", Policy = "billing.configure")]
    public async Task<IActionResult> SaveElectricity(Guid id, ElectricityUsageInput input, CancellationToken ct)
    {
        try { await cycle.SaveElectricityAsync(id, input, Actor, ct); return Ok(new { saved = true }); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = ex.Message }); }
    }
    [HttpPut("apartments/{id:guid}/utilities"), Authorize(Roles = "MANAGER", Policy = "billing.configure")]
    public async Task<IActionResult> SaveUtilities(Guid id, UtilityUsageInput input, CancellationToken ct)
    {
        try { await cycle.SaveUtilitiesAsync(id, input, Actor, ct); return Ok(new { saved = true }); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = ex.Message }); }
    }
    [HttpGet("invoices/{id:guid}"), Authorize(Roles = "ACCOUNTANT", Policy = "finance.manage")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) =>
        await cycle.DetailAsync(id, ct) is { } detail ? Ok(detail) : NotFound(new ProblemDetails { Status = 404, Title = "Không tìm thấy hóa đơn." });
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
    [HttpPost("invoices/{id:guid}/send"), Authorize(Roles = "ACCOUNTANT", Policy = "finance.manage")]
    public async Task<IActionResult> SendInvoice(Guid id, CancellationToken ct)
    {
        var key = Request.Headers["Idempotency-Key"].ToString();
        if (!string.IsNullOrEmpty(key) && (!Guid.TryParse(key, out var parsed) || parsed == Guid.Empty))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Mã yêu cầu gửi hóa đơn không hợp lệ." });
        try { return Ok(new { recipients = await cycle.SendInvoiceAsync(id, ct, string.IsNullOrEmpty(key) ? Guid.NewGuid() : Guid.Parse(key)) }); }
        catch (KeyNotFoundException ex) { return NotFound(new ProblemDetails { Status = 404, Title = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Status = 409, Title = ex.Message }); }
    }
    [HttpPost("reminders"), Authorize(Roles = "ACCOUNTANT", Policy = "finance.manage")]
    public async Task<IActionResult> Remind(ReminderRequest request, CancellationToken ct)
    {
        if (request.InvoiceIds is null || request.InvoiceIds.Length > 100 || request.RequestId == Guid.Empty) return BadRequest();
        return Ok(new { recipients = await cycle.RemindAsync(request.InvoiceIds.Distinct().ToArray(), ct, request.OverdueOnly, request.RequestId ?? Guid.NewGuid()) });
    }
}
public sealed record ReminderRequest(Guid[] InvoiceIds, bool OverdueOnly = false, Guid? RequestId = null);
