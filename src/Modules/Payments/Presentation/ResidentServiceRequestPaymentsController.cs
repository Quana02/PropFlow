using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.Modules.Payments.Presentation;

[ApiController, Route("api/v1/resident/service-requests/{serviceRequestId:guid}/payment"), Authorize(Roles = "RESIDENT")]
public sealed class ResidentServiceRequestPaymentsController(ResidentServiceRequestPayments payments) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<ActionResult<ResidentServiceRequestPaymentView>> Get(Guid serviceRequestId, CancellationToken ct)
    { try { return Ok(await payments.GetAsync(serviceRequestId, UserId, ct)); } catch (ResidentPaymentFailure failure) { return Failure(failure); } }

    [HttpGet("qr")]
    public async Task<IActionResult> Qr(Guid serviceRequestId, CancellationToken ct)
    { try { return File(await payments.QrAsync(serviceRequestId, UserId, ct), "image/png"); } catch (ResidentPaymentFailure failure) { return Failure(failure); } }

    [HttpPost("report-transfer")]
    public async Task<ActionResult<ResidentServiceRequestPaymentView>> Report(
        Guid serviceRequestId, ReportResidentServiceRequestTransferRequest request, CancellationToken ct)
    { try { return Ok(await payments.ReportAsync(serviceRequestId, UserId, request, ct)); } catch (ResidentPaymentFailure failure) { return Failure(failure); } }

    private ObjectResult Failure(ResidentPaymentFailure failure) => new(new ProblemDetails
    { Status = failure.Status, Title = failure.Message, Extensions = { ["code"] = failure.Code, ["traceId"] = HttpContext.TraceIdentifier } })
    { StatusCode = failure.Status };

    [HttpPost("sandbox-instruction"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(PaymentSettingsRateLimitPolicies.Provider)]
    public async Task<ActionResult<ResidentServiceRequestPaymentView>> Simulate(Guid serviceRequestId, CancellationToken ct)
    { try { return Ok(await payments.PrepareSandboxAsync(serviceRequestId, UserId, ct)); } catch (ResidentPaymentFailure failure) { return Failure(failure); } }

    [HttpPost("simulate-transfer")]
    public IActionResult LegacySimulation() => StatusCode(410, new ProblemDetails { Status = 410,
        Title = "Giả lập nội bộ đã được tắt. Hãy chuẩn bị nội dung thanh toán rồi tạo giao dịch trong SePay Test mode." });
}
