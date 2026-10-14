using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.ServiceRequests.Presentation;

[ApiController]
[Authorize(Policy = ServiceRequestsAuthorizationPolicies.WorkAssigned)]
[Route("api/v1/staff/service-requests")]
[Produces("application/json")]
public sealed class StaffServiceRequestsController(StaffServiceRequestHandler handler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(StaffServiceRequestListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StaffServiceRequestListResponse>> List([FromQuery] StaffServiceRequestListQuery query, CancellationToken ct) =>
        Result(await handler.ListAsync(Actor(), query, ct));

    [HttpGet("{requestNumber}")]
    [ProducesResponseType(typeof(StaffServiceRequestDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffServiceRequestDetailResponse>> Get(string requestNumber, CancellationToken ct) =>
        Result(await handler.GetAsync(Actor(), requestNumber, ct));

    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult<StaffServiceRequestDetailResponse>> Accept(Guid id, CancellationToken ct) =>
        Result(await handler.AcceptAsync(Actor(), id, ct));

    [HttpPost("{id:guid}/progress")]
    public async Task<ActionResult<StaffServiceRequestDetailResponse>> Progress(
        Guid id, [FromBody] UpdateStaffServiceRequestProgressRequest request, CancellationToken ct) =>
        Result(await handler.AddProgressAsync(Actor(), id, request, ct));

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<StaffServiceRequestDetailResponse>> Complete(
        Guid id, [FromBody] CompleteStaffServiceRequestWorkRequest request, CancellationToken ct) =>
        Result(await handler.CompleteWorkAsync(Actor(), id, request, ct));

    [HttpPost("{id:guid}/evidence")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(57_671_680)]
    public async Task<ActionResult<StaffServiceRequestDetailResponse>> Evidence(
        Guid id, [FromForm] List<IFormFile> files, CancellationToken ct)
    {
        var streams = new List<Stream>();
        try
        {
            var uploads = files.Select(file =>
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                return new StaffEvidenceUpload(file.FileName, file.ContentType, file.Length, stream);
            }).ToArray();
            return Result(await handler.UploadEvidenceAsync(Actor(), id, uploads, ct));
        }
        finally
        {
            foreach (var stream in streams) await stream.DisposeAsync();
        }
    }

    [HttpGet("{id:guid}/evidence/{evidenceId:guid}")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadEvidence(Guid id, Guid evidenceId, CancellationToken ct)
    {
        var result = await handler.DownloadEvidenceAsync(Actor(), id, evidenceId, ct);
        if (result.Outcome != StaffServiceRequestOutcome.Success || result.Value is null)
            return ProblemResult(404, result.ErrorCode!, result.Message!);
        return File(result.Value.Content, result.Value.ContentType, enableRangeProcessing: true);
    }

    private Guid Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var actorId) ? actorId : Guid.Empty;

    private ActionResult<T> Result<T>(StaffServiceRequestResult<T> result) => result.Outcome switch
    {
        StaffServiceRequestOutcome.Success => Ok(result.Value),
        StaffServiceRequestOutcome.InvalidInput => ProblemResult(400, result.ErrorCode!, result.Message!),
        StaffServiceRequestOutcome.NotFound => ProblemResult(404, result.ErrorCode!, result.Message!),
        _ => ProblemResult(409, result.ErrorCode!, result.Message!)
    };

    private ObjectResult ProblemResult(int status, string code, string detail) => StatusCode(status, Problem(status, code, detail));

    private ProblemDetails Problem(int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                400 => "Dữ liệu chưa hợp lệ",
                404 => "Không tìm thấy yêu cầu được phân công",
                409 => "Không thể cập nhật yêu cầu",
                _ => "Không thể xử lý yêu cầu"
            },
            Detail = detail
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return problem;
    }
}
