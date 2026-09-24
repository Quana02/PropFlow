using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Services;
using PropFlow.Modules.PropertyAssets.Contracts;

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
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.View)]
    public async Task<ActionResult<PagedResult<EquipmentDto>>> GetEquipments([FromQuery] EquipmentFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _equipmentService.GetEquipmentsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.View)]
    public async Task<ActionResult<EquipmentDetailDto>> GetEquipmentById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _equipmentService.GetEquipmentDetailByIdAsync(id, cancellationToken);
        if (result == null)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "equipment_not_found", "Không tìm thấy dữ liệu", "Không tìm thấy thiết bị."));
        }
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<EquipmentDto>> CreateEquipment([FromBody] CreateEquipmentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            command = command with { CreatedBy = CurrentUserId() };
            var result = await _equipmentService.CreateEquipmentAsync(command, cancellationToken);
            return CreatedAtAction(nameof(GetEquipmentById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "invalid_equipment_data", "Dữ liệu không hợp lệ", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "equipment_code_conflict", "Xung đột dữ liệu", ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<EquipmentDto>> UpdateEquipment(Guid id, [FromBody] UpdateEquipmentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            command = command with { UpdatedBy = CurrentUserId() };
            var result = await _equipmentService.UpdateEquipmentAsync(id, command, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "equipment_not_found", "Không tìm thấy dữ liệu", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "invalid_equipment_data", "Dữ liệu không hợp lệ", ex.Message));
        }
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.Manage)]
    public async Task<ActionResult<EquipmentDto>> SetEquipmentStatus(Guid id, [FromBody] SetEquipmentStatusCommand command, CancellationToken cancellationToken)
    {
        try
        {
            command = command with { UpdatedBy = CurrentUserId() };
            var result = await _equipmentService.SetEquipmentStatusAsync(id, command, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "equipment_not_found", "Không tìm thấy dữ liệu", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "invalid_equipment_status", "Trạng thái không hợp lệ", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(Problem(StatusCodes.Status409Conflict, "invalid_equipment_status_transition", "Không thể đổi trạng thái", ex.Message));
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
