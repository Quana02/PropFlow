using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Presentation;

[ApiController]
[Authorize(Policy = ServiceRequestsAuthorizationPolicies.Manage)]
[Route("api/v1/manager/service-requests")]
[Produces("application/json")]
public sealed class ManagerServiceRequestsController(
    IServiceRequestMaintenanceSource maintenanceSource,
    ManagerServiceRequestReviewHandler? reviewHandler = null) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ManagerServiceRequestListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ManagerServiceRequestListResponse>> List(
        [FromQuery] ManagerServiceRequestListQuery query, CancellationToken ct)
    {
        var result = await RequiredReviewHandler().ListAsync(query, ct);
        return result.Outcome == ManagerServiceRequestOutcome.Success
            ? Ok(result.Value)
            : ProblemResult(StatusCodes.Status400BadRequest, result.ErrorCode!, result.Message!);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ManagerServiceRequestDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagerServiceRequestDetailResponse>> Get(Guid id, CancellationToken ct)
    {
        var result = await RequiredReviewHandler().GetAsync(id, ct);
        return result.Outcome == ManagerServiceRequestOutcome.Success
            ? Ok(result.Value)
            : ProblemResult(StatusCodes.Status404NotFound, result.ErrorCode!, result.Message!);
    }

    [HttpPost("{id:guid}/review")]
    [ProducesResponseType(typeof(ManagerServiceRequestDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagerServiceRequestDetailResponse>> Review(
        Guid id, [FromBody] ReviewManagerServiceRequestRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var managerUserId))
            return ProblemResult(StatusCodes.Status401Unauthorized, "invalid_session", "Phiên đăng nhập không hợp lệ.");
        var result = await RequiredReviewHandler().ReviewAsync(id, managerUserId, request, ct);
        return result.Outcome switch
        {
            ManagerServiceRequestOutcome.Success => Ok(result.Value),
            ManagerServiceRequestOutcome.InvalidInput => ProblemResult(400, result.ErrorCode!, result.Message!),
            ManagerServiceRequestOutcome.NotFound => ProblemResult(404, result.ErrorCode!, result.Message!),
            _ => ProblemResult(409, result.ErrorCode!, result.Message!)
        };
    }

    [HttpGet("{id:guid}/property-context/options")]
    [ProducesResponseType(typeof(ManagerPropertyContextOptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagerPropertyContextOptionsResponse>> PropertyContextOptions(Guid id, CancellationToken ct)
    {
        var result = await RequiredReviewHandler().PropertyContextOptionsAsync(id, ct);
        return result.Outcome == ManagerServiceRequestOutcome.Success
            ? Ok(result.Value)
            : ProblemResult(404, result.ErrorCode!, result.Message!);
    }

    [HttpPut("{id:guid}/property-context")]
    [ProducesResponseType(typeof(ManagerServiceRequestDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagerServiceRequestDetailResponse>> ConfirmPropertyContext(
        Guid id, [FromBody] ConfirmManagerServiceRequestPropertyContextRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var managerUserId))
            return ProblemResult(401, "invalid_session", "Phiên đăng nhập không hợp lệ.");
        var result = await RequiredReviewHandler().ConfirmPropertyContextAsync(id, managerUserId, request, ct);
        return result.Outcome switch
        {
            ManagerServiceRequestOutcome.Success => Ok(result.Value),
            ManagerServiceRequestOutcome.InvalidInput => ProblemResult(400, result.ErrorCode!, result.Message!),
            ManagerServiceRequestOutcome.NotFound => ProblemResult(404, result.ErrorCode!, result.Message!),
            _ => ProblemResult(409, result.ErrorCode!, result.Message!)
        };
    }

    [HttpGet("{id:guid}/assignment/options")]
    [ProducesResponseType(typeof(ManagerServiceRequestAssignmentOptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagerServiceRequestAssignmentOptionsResponse>> AssignmentOptions(
        Guid id, CancellationToken ct)
    {
        var result = await RequiredReviewHandler().AssignmentOptionsAsync(id, ct);
        return result.Outcome == ManagerServiceRequestOutcome.Success
            ? Ok(result.Value)
            : ProblemResult(404, result.ErrorCode!, result.Message!);
    }

    [HttpPut("{id:guid}/assignment")]
    [ProducesResponseType(typeof(ManagerServiceRequestDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagerServiceRequestDetailResponse>> Assign(
        Guid id, [FromBody] AssignManagerServiceRequestRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var managerUserId))
            return ProblemResult(401, "invalid_session", "Phiên đăng nhập không hợp lệ.");
        var result = await RequiredReviewHandler().AssignAsync(id, managerUserId, request, ct);
        return result.Outcome switch
        {
            ManagerServiceRequestOutcome.Success => Ok(result.Value),
            ManagerServiceRequestOutcome.InvalidInput => ProblemResult(400, result.ErrorCode!, result.Message!),
            ManagerServiceRequestOutcome.NotFound => ProblemResult(404, result.ErrorCode!, result.Message!),
            _ => ProblemResult(409, result.ErrorCode!, result.Message!)
        };
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
            var items = await maintenanceSource.SearchAsync(search?.Trim(), limit, ct);
            return Ok(items);
        }
        catch (ArgumentException exception)
        {
            return ProblemResult(
                StatusCodes.Status400BadRequest,
                "invalid_service_request_search",
                exception.Message);
        }
    }

    private ObjectResult ProblemResult(int status, string code, string detail) => StatusCode(status, Problem(status, code, detail));
    private ManagerServiceRequestReviewHandler RequiredReviewHandler() => reviewHandler ??
        throw new InvalidOperationException("Manager service request review handler is not registered.");

    private ProblemDetails Problem(int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status401Unauthorized => "Chưa xác thực",
                StatusCodes.Status404NotFound => "Không tìm thấy yêu cầu dịch vụ",
                StatusCodes.Status409Conflict => "Không thể tiếp nhận yêu cầu",
                _ => "Thông tin yêu cầu không hợp lệ"
            },
            Detail = detail
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return problem;
    }
}
