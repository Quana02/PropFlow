using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Services;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.PropertyAssets.Presentation.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class FacilitiesController : ControllerBase
{
    private readonly IFacilityService _facilityService;

    public FacilitiesController(IFacilityService facilityService)
    {
        _facilityService = facilityService;
    }

    /// <summary>
    /// Lấy danh sách tiện ích - FE-04.3
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.View)]
    public async Task<ActionResult<PagedResult<FacilityDto>>> GetFacilities([FromQuery] FacilityFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _facilityService.GetFacilitiesAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Lấy chi tiết tiện ích - FE-04.3
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.View)]
    public async Task<ActionResult<FacilityDetailDto>> GetFacilityById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _facilityService.GetFacilityByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "facility_not_found", "Không tìm thấy dữ liệu", "Không tìm thấy tiện ích."));
        }
        return Ok(result);
    }

    /// <summary>
    /// Tạo tiện ích mới - FE-04.3
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<FacilityDto>> CreateFacility([FromBody] CreateFacilityCommand command, CancellationToken cancellationToken)
    {
        try
        {
            command = command with { CreatedBy = CurrentUserId() };
            var result = await _facilityService.CreateFacilityAsync(command, cancellationToken);
            return CreatedAtAction(nameof(GetFacilityById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "invalid_facility_data", "Dữ liệu không hợp lệ", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "facility_code_conflict", "Xung đột dữ liệu", ex.Message));
        }
    }

    /// <summary>
    /// Cập nhật thông tin tiện ích - FE-04.3
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<FacilityDto>> UpdateFacility(Guid id, [FromBody] UpdateFacilityCommand command, CancellationToken cancellationToken)
    {
        try
        {
            command = command with { UpdatedBy = CurrentUserId() };
            var result = await _facilityService.UpdateFacilityAsync(id, command, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "facility_not_found", "Không tìm thấy dữ liệu", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "invalid_facility_data", "Dữ liệu không hợp lệ", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "facility_conflict", "Xung đột dữ liệu", ex.Message));
        }
    }

    /// <summary>
    /// Đặt trạng thái tiện ích - FE-04.4 (Status Management)
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<FacilityDto>> SetFacilityStatus(Guid id, [FromBody] SetFacilityStatusCommand command, CancellationToken cancellationToken)
    {
        try
        {
            command = command with { UpdatedBy = CurrentUserId() };
            var result = await _facilityService.SetFacilityStatusAsync(id, command, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "facility_not_found", "Không tìm thấy dữ liệu", ex.Message));
        }
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirst("sub")!.Value);

    private ProblemDetails Problem(int status, string code, string title, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return problem;
    }
}
