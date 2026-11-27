using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Application.Parking;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.PropertyAssets.Presentation.Controllers;

[ApiController]
[Route("api/v1/resident/parking-areas")]
[Authorize(Roles = "RESIDENT")]
public sealed class ResidentParkingController(ParkingWorkspace workspace) : ControllerBase
{
    [HttpGet]
    public Task<PublishedParkingAreaSummary[]> List(CancellationToken ct) => workspace.ListPublishedAsync(ct);

    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PublishedParkingArea), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<PublishedParkingArea>> Get(Guid id, CancellationToken ct)
    {
        var area = await workspace.GetPublishedAsync(id, ct);
        if (area is not null) return area;

        var problem = new ProblemDetails { Status = 404, Title = "Không tìm thấy sơ đồ bãi đỗ đã xuất bản." };
        problem.Extensions["code"] = "parking_published_drawing_not_found";
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return NotFound(problem);
    }
}
