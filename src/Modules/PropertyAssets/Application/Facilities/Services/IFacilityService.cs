using PropFlow.Modules.PropertyAssets.Application.Buildings.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;

namespace PropFlow.Modules.PropertyAssets.Application.Facilities.Services;

public interface IFacilityService
{
    Task<PagedResult<FacilityDto>> GetFacilitiesAsync(FacilityFilterQuery query, CancellationToken cancellationToken = default);
    Task<FacilityDetailDto?> GetFacilityByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FacilityDto> CreateFacilityAsync(CreateFacilityCommand command, CancellationToken cancellationToken = default);
    Task<FacilityDto> UpdateFacilityAsync(Guid id, UpdateFacilityCommand command, CancellationToken cancellationToken = default);
    Task<FacilityDto> SetFacilityStatusAsync(Guid id, SetFacilityStatusCommand command, CancellationToken cancellationToken = default);
}
