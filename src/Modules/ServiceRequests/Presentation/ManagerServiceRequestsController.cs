using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Presentation;

[ApiController]
[Authorize(Policy = ServiceRequestsAuthorizationPolicies.Manage)]
[Route("api/v1/manager/service-requests")]
[Produces("application/json")]
public sealed class ManagerServiceRequestsController(IServiceRequestMaintenanceSource source) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ServiceRequestMaintenanceReference), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceRequestMaintenanceReference>> Get(Guid id, CancellationToken ct)
    {
        var request = await source.GetAsync(id, ct);
        if (request is not null)
            return Ok(request);

        return NotFound(Problem(
            StatusCodes.Status404NotFound,
            "service_request_not_found",
            "Không tìm thấy yêu cầu dịch vụ."));
    }

    [HttpGet("link-candidates")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceRequestMaintenanceReference>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ServiceRequestMaintenanceReference>>> Search(
        [FromQuery] string? search = null,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        try
        {
            var items = await source.SearchAsync(search?.Trim(), limit, ct);
            return Ok(items);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(Problem(
                StatusCodes.Status400BadRequest,
                "invalid_service_request_search",
                exception.Message));
        }
    }

    private ProblemDetails Problem(int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status404NotFound
                ? "Không tìm thấy yêu cầu dịch vụ"
                : "Thông tin tìm kiếm không hợp lệ",
            Detail = detail
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return problem;
    }
}
