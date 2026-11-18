using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Api.Composition;

public sealed class FacilityResidentAccess(IResidentResidenceSource residences, ICurrentBuildingTimeZone timezone, TimeProvider clock) : IFacilityResidentAccess
{
    public async Task<bool> ActiveAsync(Guid actor, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(await timezone.GetAsync(ct))).DateTime);
        return await residences.FindActiveAsync(actor, date, ct) is not null;
    }
}
