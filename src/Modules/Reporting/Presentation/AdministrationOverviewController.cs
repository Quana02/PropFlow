using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Reporting.Application.AdministrationOverview;

namespace PropFlow.Modules.Reporting.Presentation;

[ApiController]
[Route("api/v1/reporting/administration-overview")]
[Authorize(Roles = "ADMIN")]
public sealed class AdministrationOverviewController(AdministrationOverviewQuery query) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AdministrationAuthorizationPolicies.ViewSystemOverview)]
    [ProducesResponseType(typeof(AdministrationOverviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdministrationOverviewDto>> Get(CancellationToken ct) => Ok(await query.GetAsync(ct));
}
