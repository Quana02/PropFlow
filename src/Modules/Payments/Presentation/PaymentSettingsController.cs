using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;

namespace PropFlow.Modules.Payments.Presentation;

public static class PaymentSettingsAuthorizationPolicies
{
    public const string Manage = "payment-settings.manage";
}

[ApiController, Route("api/v1/payment-settings"), Authorize(Roles = "ADMIN", Policy = PaymentSettingsAuthorizationPolicies.Manage)]
public sealed class PaymentSettingsController(PaymentDestinationSettings settings) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);

    [HttpGet("destination")]
    public async Task<ActionResult<PaymentDestinationState>> Current(CancellationToken ct) => Ok(await settings.CurrentAsync(ct));

    [HttpGet("destination/history")]
    public Task<IReadOnlyList<PaymentDestinationView>> History(CancellationToken ct) => settings.HistoryAsync(ct);

    [HttpGet("banks")]
    public async Task<ActionResult<IReadOnlyList<BankOption>>> Banks(CancellationToken ct) =>
        await ExecuteAsync(() => settings.BanksAsync(ct));

    [HttpPost("destination/preview")]
    public Task<ActionResult<PaymentDestinationPreview>> Preview(PaymentDestinationDraft request, CancellationToken ct) =>
        Task.FromResult<ActionResult<PaymentDestinationPreview>>(SePaySelectionRequired());

    [HttpPost("destination/change-challenge"), EnableRateLimiting(PaymentSettingsRateLimitPolicies.Otp)]
    public async Task<ActionResult<PrivilegedActionChallengeResult>> Challenge(PaymentDestinationChallengeRequest request, CancellationToken ct) =>
        string.Equals(request.Action?.Trim(), "DEACTIVATE", StringComparison.OrdinalIgnoreCase)
            ? await ExecuteAsync(() => settings.RequestChallengeAsync(Actor, request, ct))
            : SePaySelectionRequired();

    [HttpPut("destination")]
    public Task<ActionResult<PaymentDestinationView>> Save(ConfirmPaymentDestinationRequest request, CancellationToken ct) =>
        Task.FromResult<ActionResult<PaymentDestinationView>>(SePaySelectionRequired());

    [HttpPost("destination/deactivate")]
    public async Task<IActionResult> Deactivate(DeactivatePaymentDestinationRequest request, CancellationToken ct)
    {
        try
        {
            await settings.DeactivateAsync(Actor, request, ct);
            return NoContent();
        }
        catch (PaymentSettingsFailure failure) { return Problem(failure); }
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (PaymentSettingsFailure failure) { return Problem(failure); }
    }

    private ObjectResult Problem(PaymentSettingsFailure failure) => new(new ProblemDetails
    {
        Status = failure.Status,
        Title = failure.Message,
        Extensions = { ["code"] = failure.Code, ["traceId"] = HttpContext.TraceIdentifier }
    }) { StatusCode = failure.Status };

    private ObjectResult SePaySelectionRequired() => Problem(new PaymentSettingsFailure(409,
        "sepay_selection_required", "Vui lòng đồng bộ và chọn tài khoản từ SePay để cấu hình nhận tiền."));
}
