using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, AllowAnonymous, Route("api/v1/payments/webhooks/sepay/sandbox")]
public sealed class SePaySandboxWebhookController(SePaySandboxWebhook webhook) : ControllerBase
{
    [HttpPost, RequestSizeLimit(16384)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var body = new MemoryStream(); var buffer = new byte[4096]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) > 0)
        { if (body.Length + count > 16384) return StatusCode(413); await body.WriteAsync(buffer.AsMemory(0, count), ct); }
        SePayTransfer? transfer;
        try { transfer = JsonSerializer.Deserialize<SePayTransfer>(body.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return BadRequest(new { success = false }); }
        if (transfer is null) return BadRequest(new { success = false });
        try { var outcome = await webhook.ReceiveAsync(Request.Headers.Authorization.ToString(), transfer, ct); return Ok(new { success = true, outcome }); }
        catch (PaymentSettingsFailure e) { return StatusCode(e.Status, new { success = false, code = e.Code }); }
    }
}
