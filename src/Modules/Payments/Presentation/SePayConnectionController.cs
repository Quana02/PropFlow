using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, Route("api/v1/payment-settings/sepay"), Authorize(Roles = "ADMIN", Policy = PaymentSettingsAuthorizationPolicies.Manage)]
public sealed class SePayConnectionController(SePayConnectionSettings settings) : ControllerBase
{
    [HttpGet("connection"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Read)]
    public async Task<ActionResult<SePayConnectionState>> Current(CancellationToken ct) => Ok(await settings.CurrentAsync(ct));

    [HttpPost("connection/check"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Provider)]
    public async Task<ActionResult<SePayConnectionState>> Check(CancellationToken ct) => Ok(await settings.CheckAsync(ct));
}
