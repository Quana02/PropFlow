using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public interface IFacilityApiClient
{
    Task<ApiResult<PagedResult<FacilityModel>>> GetFacilitiesAsync(FacilityFilterModel filter, CancellationToken cancellationToken = default);
    Task<ApiResult<FacilityDetailModel>> GetFacilityByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResult<FacilityModel>> CreateFacilityAsync(CreateFacilityModel model, CancellationToken cancellationToken = default);
    Task<ApiResult<FacilityModel>> UpdateFacilityAsync(Guid id, UpdateFacilityModel model, CancellationToken cancellationToken = default);
    Task<ApiResult<FacilityModel>> SetFacilityStatusAsync(Guid id, MasterDataStatus status, CancellationToken cancellationToken = default);
}
