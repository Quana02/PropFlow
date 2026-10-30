using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, Route("api/v1/payment-settings/sepay/sandbox"),
 Authorize(Roles = "ADMIN", Policy = PaymentSettingsAuthorizationPolicies.Manage), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Provider)]
public sealed class SePaySandboxController(SePaySandbox sandbox) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("status"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Read)]
    public Task<ActionResult<SePaySandboxState>> State(CancellationToken ct) => Execute(() => sandbox.StateAsync(ct));
    [HttpPost("preview"), RequestSizeLimit(8192)]
    public Task<ActionResult<SePaySandboxProbe>> Preview(SePaySandboxDraft request, CancellationToken ct) => Execute(() => sandbox.PreviewAsync(request, ct));
    [HttpPost("challenge"), RequestSizeLimit(8192), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Otp)]
    public Task<ActionResult<PrivilegedActionChallengeResult>> Challenge(SePaySandboxDraft request, CancellationToken ct) => Execute(() => sandbox.ChallengeAsync(Actor, request, ct));
    [HttpPost("confirm"), RequestSizeLimit(8192)]
    public Task<ActionResult<SePaySandboxState>> Confirm(SePaySandboxConfirm request, CancellationToken ct) => Execute(() => sandbox.ConfirmAsync(Actor, request, ct));
    [HttpPost("check")]
    public Task<ActionResult<SePaySandboxProbe>> Check(CancellationToken ct) => Execute(() => sandbox.CheckAsync(ct));
    [HttpGet("accounts")]
    public Task<ActionResult<IReadOnlyList<SePaySandboxAccount>>> Accounts(CancellationToken ct) => Execute(() => sandbox.AccountsAsync(ct));
    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await action()); }
        catch (PaymentSettingsFailure failure)
        { return StatusCode(failure.Status, new ProblemDetails { Status = failure.Status, Title = failure.Message,
            Extensions = { ["code"] = failure.Code, ["traceId"] = HttpContext.TraceIdentifier } }); }
    }
}
