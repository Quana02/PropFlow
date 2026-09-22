using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Services;
using PropFlow.Modules.Apartments.Application;

namespace PropFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "MANAGER,STAFF")]
[Route("api/v1/buildings/current")]
[Produces("application/json")]
public class CurrentBuildingController : ControllerBase
{
    private readonly IBuildingService _buildingService;
    private readonly IApartmentStatisticsReader _apartmentStatisticsReader;

    public CurrentBuildingController(IBuildingService buildingService, IApartmentStatisticsReader apartmentStatisticsReader)
    {
        _buildingService = buildingService ?? throw new ArgumentNullException(nameof(buildingService));
        _apartmentStatisticsReader = apartmentStatisticsReader ?? throw new ArgumentNullException(nameof(apartmentStatisticsReader));
    }

    /// <summary>
    /// Lấy thông tin tổng quan chung cư hiện tại (Building Profile + Operational Summary) - FE-04.1
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(CurrentBuildingOverviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default)
    {
        var overview = await _buildingService.GetCurrentBuildingOverviewAsync(cancellationToken);
        if (overview is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Không tìm thấy dữ liệu",
                Detail = "Không tìm thấy thông tin chung cư. Vui lòng thiết lập dữ liệu khởi tạo."
            });
        }

        var totalApartments = await _apartmentStatisticsReader.GetTotalApartmentsAsync(cancellationToken);
        var response = new CurrentBuildingOverviewResponse(
            overview.Id,
            overview.Code,
            overview.Name,
            overview.Address,
            overview.TimeZoneId,
            overview.NumberOfFloors,
            overview.Description,
            overview.Status,
            totalApartments,
            overview.Facilities,
            overview.Equipment,
            overview.CreatedAt,
            overview.UpdatedAt
        );

        return Ok(response);
    }

    /// <summary>
    /// Cập nhật thông tin chung cư hiện tại - Single-Building Architecture
    /// </summary>
    [HttpPut]
    [Authorize(Roles = "MANAGER")]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateCurrent(
        [FromBody] UpdateCurrentBuildingCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _buildingService.UpdateCurrentBuildingAsync(command, cancellationToken);
            return Ok(result);
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
}
