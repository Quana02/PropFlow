using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Services;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;

namespace PropFlow.Modules.PropertyAssets.Presentation.Controllers;

[ApiController]
[Route("api/v1/buildings")]
[Produces("application/json")]
public class BuildingsController : ControllerBase
{
    private readonly IBuildingService _buildingService;

    public BuildingsController(IBuildingService buildingService)
    {
        _buildingService = buildingService ?? throw new ArgumentNullException(nameof(buildingService));
    }

    /// <summary>
    /// Lấy danh sách tòa nhà có hỗ trợ tìm kiếm và phân trang
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<BuildingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBuildings(
        [FromQuery] string? searchKeyword,
        [FromQuery] MasterDataStatus? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new BuildingFilterQuery(searchKeyword, status, pageIndex, pageSize);
        var result = await _buildingService.GetBuildingsAsync(query, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Lấy thông tin chi tiết một tòa nhà theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var building = await _buildingService.GetBuildingByIdAsync(id, cancellationToken);
        if (building is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy dữ liệu",
                Detail = $"Không tìm thấy tòa nhà có ID = '{id}'."
            });
        }

        return Ok(building);
    }

    /// <summary>
    /// Lấy thông tin chi tiết mở rộng của tòa nhà (kèm thống kê tài sản) - FE-04.2
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    [ProducesResponseType(typeof(BuildingDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetailById(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await _buildingService.GetBuildingDetailByIdAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy dữ liệu",
                Detail = $"Không tìm thấy chi tiết tòa nhà có ID = '{id}'."
            });
        }

        return Ok(detail);
    }

    /// <summary>
    /// Tạo mới một hồ sơ tòa nhà
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBuildingCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _buildingService.CreateBuildingAsync(command, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dữ liệu không hợp lệ",
                Detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Cập nhật thông tin tòa nhà
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBuildingCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _buildingService.UpdateBuildingAsync(id, command, cancellationToken);
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
    }

    /// <summary>
    /// Thay đổi trạng thái (Hoạt động / Ngưng hoạt động) tòa nhà
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStatus(
        Guid id,
        [FromBody] SetBuildingStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _buildingService.SetBuildingStatusAsync(id, command, cancellationToken);
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
    }
}
