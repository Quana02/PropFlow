using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Complaints.Application;
using PropFlow.Modules.Administration.Contracts;

namespace PropFlow.Modules.Complaints.Presentation;

[ApiController, Route("api/v1/complaints"), Authorize(Policy = "complaints.access")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
[ProducesResponseType(typeof(ProblemDetails), 409)]
public sealed class ComplaintsController(ComplaintUseCases useCases, IComplaintContextSource contexts,
    IMaintenanceStaffDirectory staff, IAuthorizationService authorization) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue("sub")!);
    private async Task<string> ViewerAsync() => (await authorization.AuthorizeAsync(User, "complaints.manage")).Succeeded ? "MANAGER"
        : (await authorization.AuthorizeAsync(User, "complaints.work")).Succeeded ? "STAFF" : "RESIDENT";

    [HttpGet]
    [ProducesResponseType(typeof(ComplaintPage), 200)]
    public async Task<IActionResult> List(string? search, string? status, int page = 1, CancellationToken ct = default) =>
        Map(await useCases.ListAsync(Actor, await ViewerAsync(), search, status, page, ct));
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ComplaintDetail), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Map(await useCases.GetAsync(Actor, await ViewerAsync(), id, ct));
    [HttpPost, Authorize(Roles = "RESIDENT")]
    [ProducesResponseType(typeof(ComplaintDetail), 201)]
    public Task<IActionResult> Submit(SubmitComplaint request, CancellationToken ct) => Execute(() => useCases.SubmitAsync(Actor, request, ct));
    [HttpPost("{id:guid}/review"), Authorize(Policy = "complaints.manage")]
    [ProducesResponseType(typeof(ComplaintDetail), 200)]
    public Task<IActionResult> Review(Guid id, CancellationToken ct) => Execute(() => useCases.ReviewAsync(Actor, id, ct));
    [HttpPut("{id:guid}/context"), Authorize(Policy = "complaints.manage")]
    [ProducesResponseType(typeof(ComplaintDetail), 200)]
    public Task<IActionResult> Context(Guid id, ComplaintContext request, CancellationToken ct) => Execute(() => useCases.ContextAsync(Actor, id, request, ct));
    [HttpGet("context-options"), Authorize(Policy = "complaints.manage")]
    [ProducesResponseType(typeof(ComplaintContextOptions), 200)]
    public async Task<IActionResult> ContextOptions(string? search, CancellationToken ct) => Ok(await contexts.OptionsAsync(search, ct));
    [HttpGet("staff-options"), Authorize(Policy = "complaints.manage")]
    [ProducesResponseType(typeof(IReadOnlyList<MaintenanceStaffRecord>), 200)]
    public async Task<IActionResult> StaffOptions(CancellationToken ct) => Ok(await staff.GetActiveStaffAsync(ct));
    [HttpPost("{id:guid}/followups"), Authorize(Policy = "complaints.manage")]
    [ProducesResponseType(typeof(ComplaintDetail), 200)]
    public Task<IActionResult> Assign(Guid id, AssignComplaintFollowup request, CancellationToken ct) => Execute(() => useCases.AssignAsync(Actor, id, request, ct));
    [HttpPut("{id:guid}/followups/{followupId:guid}"), Authorize(Policy = "complaints.work")]
    [ProducesResponseType(typeof(ComplaintDetail), 200)]
    public Task<IActionResult> Update(Guid id, Guid followupId, FollowupUpdate request, CancellationToken ct) => Execute(() => useCases.UpdateFollowupAsync(Actor, id, followupId, request, ct));
    [HttpPost("{id:guid}/resolve"), Authorize(Policy = "complaints.manage")]
    [ProducesResponseType(typeof(ComplaintDetail), 200)]
    public Task<IActionResult> Resolve(Guid id, OfficialComplaintResponse request, CancellationToken ct) => Execute(() => useCases.ResolveAsync(Actor, id, request, ct));
    [HttpPost("{id:guid}/close"), Authorize(Policy = "complaints.manage")]
    [ProducesResponseType(typeof(ComplaintDetail), 200)]
    public Task<IActionResult> Close(Guid id, CancellationToken ct) => Execute(() => useCases.CloseAsync(Actor, id, ct));

    private async Task<IActionResult> Execute(Func<Task<ComplaintResult<ComplaintDetail>>> action)
    {
        try { return Map(await action()); }
        catch (DbUpdateConcurrencyException) { return Map(new ComplaintResult<ComplaintDetail>(409, Message: "Phản ánh vừa được cập nhật. Vui lòng tải lại.")); }
    }
    private IActionResult Map<T>(ComplaintResult<T> result)
    {
        if (result.Status < 400) return StatusCode(result.Status, result.Data);
        var problem = new ProblemDetails { Status = result.Status, Title = "Không thể thực hiện thao tác phản ánh", Detail = result.Message };
        problem.Extensions["code"] = $"complaint_{result.Status}";
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(result.Status, problem);
    }
}
