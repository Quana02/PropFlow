using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Services;

namespace PropFlow.Modules.PropertyAssets.Presentation.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class EquipmentsController : ControllerBase
{
    private readonly IEquipmentService _equipmentService;

    public EquipmentsController(IEquipmentService equipmentService)
    {
        _equipmentService = equipmentService;
    }

    [HttpGet]
    [Authorize(Roles = "MANAGER,STAFF")]
    public async Task<ActionResult<PagedResult<EquipmentDto>>> GetEquipments([FromQuery] EquipmentFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _equipmentService.GetEquipmentsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "MANAGER,STAFF")]
    public async Task<ActionResult<EquipmentDetailDto>> GetEquipmentById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _equipmentService.GetEquipmentDetailByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = "Không tìm thấy thiết bị." });
        }
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "MANAGER")]
    public async Task<ActionResult<EquipmentDto>> CreateEquipment([FromBody] CreateEquipmentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _equipmentService.CreateEquipmentAsync(command, cancellationToken);
            return CreatedAtAction(nameof(GetEquipmentById), new { id = result.Id }, result);
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
    [Authorize(Roles = "MANAGER")]
    public async Task<ActionResult<EquipmentDto>> UpdateEquipment(Guid id, [FromBody] UpdateEquipmentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _equipmentService.UpdateEquipmentAsync(id, command, cancellationToken);
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
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "MANAGER")]
    public async Task<ActionResult<EquipmentDto>> SetEquipmentStatus(Guid id, [FromBody] SetEquipmentStatusCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _equipmentService.SetEquipmentStatusAsync(id, command, cancellationToken);
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
    }
}
