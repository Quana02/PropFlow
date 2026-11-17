using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.AiRecommendation.Application.UtilityConsumption;
using PropFlow.Modules.AiRecommendation.Infrastructure.UtilityConsumption;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.AiRecommendation.Presentation;

// Task 1 scope extension: no financial state is changed by these endpoints.
[ApiController, Authorize, Produces("application/json")]
public sealed class UtilityConsumptionController(UtilityInsights insights, ICurrentBuildingTimeZone timeZone, TimeProvider clock) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet("api/v1/utility-insights"), Authorize(Roles = "MANAGER,ACCOUNTANT", Policy = "billing.read")]
    [ProducesResponseType(typeof(UtilityInsightPage), 200)]
    public Task<IActionResult> List(DateOnly month, CancellationToken ct, int page = 1, int pageSize = 20, string? search = null) =>
        Run(async () => { await Validate(month, ct); return await insights.ListAsync(month, page, pageSize, search, ct); });
    [HttpGet("api/v1/utility-insights/apartments/{apartment:guid}"), Authorize(Roles = "MANAGER,ACCOUNTANT", Policy = "billing.read")]
    [ProducesResponseType(typeof(UtilityInsightView), 200)]
    public Task<IActionResult> Detail(Guid apartment, DateOnly month, CancellationToken ct) =>
        Run(async () => { await Validate(month, ct); return await insights.GetAsync(apartment, month, ct); });
    [HttpPost("api/v1/utility-insights/apartments/{apartment:guid}/explain"), Authorize(Roles = "MANAGER,ACCOUNTANT", Policy = "billing.read")]
    [ProducesResponseType(typeof(UtilityInsightView), 200), ProducesResponseType(429)]
    public Task<IActionResult> Explain(Guid apartment, DateOnly month, UtilityExplainInput request, CancellationToken ct) =>
        Run(async () => { await Validate(month, ct); return await insights.ExplainAsync(apartment, month, request, Actor, ct); });
    [HttpPost("api/v1/utility-insights/apartments/{apartment:guid}/review"), Authorize(Roles = "MANAGER", Policy = "billing.configure")]
    [ProducesResponseType(typeof(UtilityInsightView), 200), ProducesResponseType(409)]
    public Task<IActionResult> Review(Guid apartment, DateOnly month, UtilityReviewInput request, CancellationToken ct) =>
        Run(async () => { await Validate(month, ct); return await insights.ReviewAsync(apartment, month, request, Actor, ct); });
    [HttpGet("api/v1/resident/utility-insights"), Authorize(Roles = "RESIDENT")]
    [ProducesResponseType(typeof(UtilityInsightView[]), 200)]
    public Task<IActionResult> Resident(CancellationToken ct) => Run(() => insights.ResidentAsync(Actor, ct));
    private async Task Validate(DateOnly month, CancellationToken ct)
    {
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(await timeZone.GetAsync(ct)));
        if (month.Year < 2000 || month.Day != 1 || month > new DateOnly(now.Year, now.Month, 1))
            throw new UtilityInsightFailure(400, "invalid_month", "Chọn tháng sử dụng hợp lệ, không nằm trong tương lai.");
    }
    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (UtilityInsightFailure ex)
        {
            var problem = new ProblemDetails { Status = ex.Status, Title = ex.Message };
            problem.Extensions["code"] = ex.Code; problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            return StatusCode(ex.Status, problem);
        }
    }
}
