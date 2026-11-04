using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using System.Text.Json;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, AllowAnonymous, Route("api/v1/payments/webhooks/sepay")]
public sealed class SePayWebhookController(SePayInbox inbox, IOptions<SePayOptions> options, TimeProvider clock) : ControllerBase
{
    [HttpPost, RequestSizeLimit(16384)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!options.Value.Enabled) return StatusCode(503, new { success = false });
        // Read original bytes for HMAC; never reconstruct a signed JSON payload.
        using var body = new MemoryStream();
        var buffer = new byte[4096]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) > 0)
        { if (body.Length + count > 16384) return StatusCode(413); await body.WriteAsync(buffer.AsMemory(0, count), ct); }
        var bytes = body.ToArray();
        var authorized = options.Value.AuthenticationMode == "HMAC"
            ? SePayVerification.Signed(bytes, Request.Headers["X-SePay-Signature"].ToString(), Request.Headers["X-SePay-Timestamp"].ToString(), options.Value, clock.GetUtcNow())
            : SePayVerification.Authorized(Request.Headers.Authorization.ToString(), options.Value);
        if (!authorized) return Unauthorized(new { success = false });
        SePayTransfer? transfer;
        try { transfer = JsonSerializer.Deserialize<SePayTransfer>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return BadRequest(new { success = false }); }
        if (transfer is null) return BadRequest(new { success = false });
        if (transfer.ReferenceCode?.Length > 100 || transfer.Content?.Length > 4000 || transfer.Code?.Length > 100 ||
            transfer.AccountNumber?.Length > 50 || transfer.Gateway?.Length > 100 || transfer.TransactionDate?.Length > 50 ||
            transfer.SubAccount?.Length > 100) return BadRequest(new { success = false });
        try { await inbox.ReceiveAsync(transfer, ct); }
        catch (PaymentSettingsFailure failure) { return StatusCode(failure.Status, new { success = false, code = failure.Code }); }
        return Ok(new { success = true });
    }
}
