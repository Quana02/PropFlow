using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Services;

namespace PropFlow.Modules.PropertyAssets.Presentation.Controllers;

[ApiController]
[Authorize(Roles = "MANAGER")]
[Route("api/v1/[controller]")]
public class FacilitiesController : ControllerBase
{
    private readonly IFacilityService _facilityService;

    public FacilitiesController(IFacilityService facilityService)
    {
        _facilityService = facilityService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<FacilityDto>>> GetFacilities([FromQuery] FacilityFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _facilityService.GetFacilitiesAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FacilityDetailDto>> GetFacilityById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _facilityService.GetFacilityByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = "Không tìm thấy tiện ích." });
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<FacilityDto>> CreateFacility([FromBody] CreateFacilityCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _facilityService.CreateFacilityAsync(command, cancellationToken);
            return CreatedAtAction(nameof(GetFacilityById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dữ liệu không hợp lệ",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Xung đột dữ liệu",
                Detail = ex.Message
            });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<FacilityDto>> UpdateFacility(Guid id, [FromBody] UpdateFacilityCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _facilityService.UpdateFacilityAsync(id, command, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy dữ liệu",
                Detail = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dữ liệu không hợp lệ",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Xung đột dữ liệu",
                Detail = ex.Message
            });
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<FacilityDto>> SetFacilityStatus(Guid id, [FromBody] SetFacilityStatusCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _facilityService.SetFacilityStatusAsync(id, command, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
