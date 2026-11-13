using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Api.Composition;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Api.Controllers;

[ApiController, Route("api/v1/resident/weather"), Authorize(Roles = "RESIDENT")]
[Produces("application/json")]
public sealed class ResidentWeatherController(CurrentBuildingWeatherService weather) : ControllerBase
{
    [HttpGet, ProducesResponseType(typeof(CurrentBuildingWeatherResponse), 200)]
    [ProducesResponseType(401), ProducesResponseType(403)]
    public async Task<ActionResult<CurrentBuildingWeatherResponse>> Get(CancellationToken ct) => Ok(await weather.GetAsync(ct));
}
