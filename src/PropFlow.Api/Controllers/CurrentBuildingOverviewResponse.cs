using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;

namespace PropFlow.Api.Controllers;

public record CurrentBuildingOverviewResponse(
    Guid Id,
    string Code,
    string Name,
    string Address,
    string TimeZoneId,
    int NumberOfFloors,
    string? Description,
    MasterDataStatus Status,
    int TotalApartments,
    FacilitySummaryDto Facilities,
    EquipmentSummaryDto Equipment,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
