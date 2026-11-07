using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Api.Composition;

public sealed class ChatResidentScope(IResidentResidenceSource residences, ICurrentBuildingTimeZone timezone,
    TimeProvider clock) : IChatResidentScope
{
    public async Task<bool> ActiveAsync(Guid actor, CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await timezone.GetAsync(ct));
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        return await residences.FindActiveAsync(actor, date, ct) is not null;
    }
}
