using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Buildings.Services;
using PropFlow.Modules.Apartments.Application;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Api.Controllers;

[ApiController]
[Authorize(Policy = PropertyAssetsAuthorizationPolicies.View)]
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
            return NotFound(Problem(StatusCodes.Status404NotFound, "building_not_configured", "Không tìm thấy dữ liệu", "Không tìm thấy thông tin chung cư. Vui lòng thiết lập dữ liệu khởi tạo."));
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
    /// Cập nhật hồ sơ chung cư hiện hành của deployment.
    /// </summary>
    [HttpPut]
    [Authorize(Policy = PropertyAssetsAuthorizationPolicies.Manage)]
    [ProducesResponseType(typeof(BuildingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateCurrent(
        [FromBody] UpdateCurrentBuildingCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            command = command with { UpdatedBy = CurrentUserId() };
            var result = await _buildingService.UpdateCurrentBuildingAsync(command, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "building_not_configured", "Dữ liệu không hợp lệ", ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Problem(StatusCodes.Status400BadRequest, "invalid_building_data", "Dữ liệu không hợp lệ", ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(Problem(StatusCodes.Status404NotFound, "building_not_found", "Không tìm thấy dữ liệu", ex.Message));
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
