using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, Route("api/v1/payment-settings/sepay/accounts"), Authorize(Roles = "ADMIN", Policy = PaymentSettingsAuthorizationPolicies.Manage)]
public sealed class SePayAccountsController(SePayAccountSelection selection) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet, EnableRateLimiting(PaymentSettingsRateLimitPolicies.Provider)]
    public Task<ActionResult<IReadOnlyList<SePayBankAccountView>>> List(CancellationToken ct) => Execute(() => selection.ListAsync(ct));
    [HttpPost("preview"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Provider)]
    public Task<ActionResult<PaymentDestinationPreview>> Preview(SelectSePayAccountRequest request, CancellationToken ct) => Execute(() => selection.PreviewAsync(request, ct));
    [HttpPost("change-challenge"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Otp)]
    public Task<ActionResult<PrivilegedActionChallengeResult>> Challenge(SelectSePayAccountRequest request, CancellationToken ct) => Execute(() => selection.ChallengeAsync(Actor, request, ct));
    [HttpPut("destination"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Provider)]
    public Task<ActionResult<PaymentDestinationView>> Save(ConfirmSePayAccountRequest request, CancellationToken ct) => Execute(() => selection.SaveAsync(Actor, request, ct));

    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (PaymentSettingsFailure failure)
        { return StatusCode(failure.Status, new ProblemDetails { Status = failure.Status, Title = failure.Message,
            Extensions = { ["code"] = failure.Code, ["traceId"] = HttpContext.TraceIdentifier } }); }
    }
}
