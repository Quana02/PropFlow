using PropFlow.Web.Client.Features.Facility.Models;

namespace PropFlow.Web.Client.Features.Facility.Services;

public interface IFacilityApiClient
{
    Task<PagedResult<FacilityModel>> GetFacilitiesAsync(FacilityFilterModel filter, CancellationToken cancellationToken = default);
    Task<FacilityDetailModel?> GetFacilityByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FacilityModel> CreateFacilityAsync(CreateFacilityModel model, CancellationToken cancellationToken = default);
    Task<FacilityModel> UpdateFacilityAsync(Guid id, UpdateFacilityModel model, CancellationToken cancellationToken = default);
    Task<FacilityModel> SetFacilityStatusAsync(Guid id, MasterDataStatus status, CancellationToken cancellationToken = default);
}
