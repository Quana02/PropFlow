using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, Route("api/v1/payment-settings/sepay/oauth"),
 Authorize(Roles = "ADMIN", Policy = PaymentSettingsAuthorizationPolicies.Manage), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Provider)]
public sealed class SePayOAuthController(SePayOAuth oauth) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("status"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Read)]
    public Task<ActionResult<SePayOAuthState>> State(CancellationToken ct) => Execute(() => oauth.StateAsync(ct));
    [HttpPost("challenge/{action}"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Otp)]
    public Task<ActionResult<PrivilegedActionChallengeResult>> Challenge(string action, CancellationToken ct) => Execute(() => oauth.ChallengeAsync(Actor, action.ToUpperInvariant(), ct));
    [HttpPost("start")]
    public Task<ActionResult<SePayOAuthStart>> Start(SePayOAuthConfirm request, CancellationToken ct) => Execute(() => oauth.StartAsync(Actor, request, ct));
    [HttpPost("callback")]
    public Task<ActionResult<SePayOAuthState>> Callback(SePayOAuthCallback request, CancellationToken ct) => Execute(async () =>
    { await oauth.CompleteAsync(Actor, request, ct); return await oauth.StateAsync(ct); });
    [HttpPost("webhook")]
    public Task<ActionResult<SePayOAuthState>> Webhook(SePayOAuthConfirm request, CancellationToken ct) => Execute(async () =>
    { await oauth.ConfigureWebhookAsync(Actor, request, ct); return await oauth.StateAsync(ct); });

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (PaymentSettingsFailure failure)
        { return StatusCode(failure.Status, new ProblemDetails { Status = failure.Status, Title = failure.Message,
            Extensions = { ["code"] = failure.Code, ["traceId"] = HttpContext.TraceIdentifier } }); }
    }
}
