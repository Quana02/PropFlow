using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Contracts;

namespace PropFlow.Modules.Billing.Presentation;
[ApiController, Route("api/v1/billing/rental-pricing"), Authorize(Policy = "billing.configure")]
public sealed class RentalPricingController(IRentalPricing pricing, IBillingHouseholdDirectory apartments, IPricingChangeDelivery delivery) : ControllerBase
{
    [HttpGet("apartments")]
    public Task<IReadOnlyList<BillingApartment>> Apartments(CancellationToken ct) => apartments.ManagedApartmentsAsync(ct);
    [HttpGet]
    public Task<IReadOnlyList<RentalPricingView>> List(Guid? apartmentId, Guid? apartmentTypeId, CancellationToken ct) => apartmentTypeId is Guid typeId ? pricing.ListForTypeAsync(typeId, ct) : pricing.ListAsync(apartmentId, ct);
    [HttpPost]
    public async Task<IActionResult> Save(RentalPricingInput input, CancellationToken ct)
    {
        try { await pricing.SaveAsync(input, Guid.Parse(User.FindFirstValue("sub")!), ct); await delivery.TryDispatchAsync(ct); return Ok(new { saved = true }); }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Status = 400, Title = ex.Message }); }
    }
}
