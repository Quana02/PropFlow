using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.ServiceRequests.Application.GetResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Application.ListResidentServiceRequests;
using PropFlow.Modules.ServiceRequests.Application.RateResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Application.SubmitServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Presentation;

[ApiController]
[Authorize(Policy = ServiceRequestsAuthorizationPolicies.SubmitAsResident)]
[Route("api/v1/resident/service-requests")]
[Produces("application/json")]
public sealed class ResidentServiceRequestsController(
    SubmitServiceRequestHandler submitHandler,
    ListResidentServiceRequestsHandler listHandler,
    GetResidentServiceRequestHandler detailHandler,
    RateResidentServiceRequestHandler rateHandler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ResidentServiceRequestListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ResidentServiceRequestListItem>>> List(
        [FromQuery] ResidentServiceRequestListQuery query,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId))
            return Problem(StatusCodes.Status401Unauthorized, "invalid_session", "Phiên đăng nhập không hợp lệ.");

        var result = await listHandler.HandleAsync(userId, query, ct);
        return result.Outcome switch
        {
            ListResidentServiceRequestsOutcome.Success => Ok(result.Items),
            ListResidentServiceRequestsOutcome.InvalidInput =>
                Problem(StatusCodes.Status400BadRequest, result.ErrorCode!, result.Message!),
            _ => Problem(StatusCodes.Status403Forbidden, result.ErrorCode!, result.Message!)
        };
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResidentServiceRequestDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResidentServiceRequestDetailResponse>> Detail(Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId))
            return Problem(StatusCodes.Status401Unauthorized, "invalid_session", "Phiên đăng nhập không hợp lệ.");

        var result = await detailHandler.HandleAsync(userId, id, ct);
        return result.Outcome switch
        {
            GetResidentServiceRequestOutcome.Success => Ok(result.Detail),
            GetResidentServiceRequestOutcome.ResidentResidenceNotFound =>
                Problem(StatusCodes.Status403Forbidden, result.ErrorCode!, result.Message!),
            _ => Problem(StatusCodes.Status404NotFound, result.ErrorCode!, result.Message!)
        };
    }

    [HttpPost]
    [EnableRateLimiting("resident-service-request-submit")]
    [ProducesResponseType(typeof(ResidentServiceRequestCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ResidentServiceRequestCreatedResponse>> Create(
        [FromBody] CreateResidentServiceRequestRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId))
            return Problem(StatusCodes.Status401Unauthorized, "invalid_session", "Phiên đăng nhập không hợp lệ.");

        var result = await submitHandler.HandleAsync(userId, request, ct);
        return result.Outcome switch
        {
            SubmitServiceRequestOutcome.Created => StatusCode(StatusCodes.Status201Created, result.Response),
            SubmitServiceRequestOutcome.InvalidInput =>
                Problem(StatusCodes.Status400BadRequest, result.ErrorCode!, result.Message!),
            SubmitServiceRequestOutcome.ResidentResidenceNotFound =>
                Problem(StatusCodes.Status403Forbidden, result.ErrorCode!, result.Message!),
            _ => Problem(StatusCodes.Status409Conflict, result.ErrorCode!, result.Message!)
        };
    }

    [HttpPut("{id:guid}/feedback")]
    [ProducesResponseType(typeof(ResidentServiceRequestFeedbackResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResidentServiceRequestFeedbackResponse>> Rate(
        Guid id,
        [FromBody] RateResidentServiceRequestRequest request,
        CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId))
            return Problem(StatusCodes.Status401Unauthorized, "invalid_session", "Phiên đăng nhập không hợp lệ.");

        var result = await rateHandler.HandleAsync(userId, id, request, ct);
        return result.Outcome switch
        {
            RateResidentServiceRequestOutcome.Saved => Ok(result.Response),
            RateResidentServiceRequestOutcome.InvalidInput => Problem(StatusCodes.Status400BadRequest, result.ErrorCode!, result.Message!),
            RateResidentServiceRequestOutcome.ResidentResidenceNotFound => Problem(StatusCodes.Status403Forbidden, result.ErrorCode!, result.Message!),
            RateResidentServiceRequestOutcome.RequestNotFound => Problem(StatusCodes.Status404NotFound, result.ErrorCode!, result.Message!),
            _ => Problem(StatusCodes.Status409Conflict, result.ErrorCode!, result.Message!)
        };
    }

    private ObjectResult Problem(int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Thông tin yêu cầu không hợp lệ",
                StatusCodes.Status401Unauthorized => "Chưa xác thực",
                StatusCodes.Status403Forbidden => "Không thể xác định căn hộ cư trú",
                StatusCodes.Status404NotFound => "Không tìm thấy yêu cầu",
                StatusCodes.Status409Conflict => "Loại dịch vụ chưa sẵn sàng",
                _ => "Dịch vụ tạm thời chưa sẵn sàng"
            },
            Detail = detail
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
