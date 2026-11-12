using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.Api.Composition;

public sealed class SupportChatActors(IAccountAccess access, IInternalAccountDirectory accounts, IResidentResidenceSource residences,
    ICurrentBuildingTimeZone timezone, TimeProvider clock, PropFlow.Modules.Apartments.Contracts.IApartmentResidentRelationshipSource apartments) : ISupportChatActors
{
    public async Task<SupportActor?> FindAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) return null;
        var grants = await access.GetAsync(id, ct);
        if (grants is null || (grants.Role != "MANAGER" && grants.Role != "RESIDENT")) return null;
        if (!grants.Permissions.Contains(grants.Role == "MANAGER" ? "MANAGE_OPERATIONS" : "USE_RESIDENT_SERVICES")) return null;
        var account = (await accounts.GetAsync([id], ct)).SingleOrDefault();
        if (account?.Status != "ACTIVE") return null;
        var zone = await timezone.GetAsync(ct);
        if (grants.Role == "MANAGER") return new(id, "MANAGER", account.DisplayName, TimeZoneId: zone);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
        var resident = await residences.FindActiveAsync(id, date, ct);
        if (resident is null) return null;
        var apartment = (await apartments.GetApartmentsAsync([resident.ApartmentUnitId], ct)).SingleOrDefault();
        return new(id, "RESIDENT", account.DisplayName, resident.ResidentId, zone,
            apartment is null ? null : $"{apartment.UnitNumber} · Tầng {apartment.FloorNumber}");
    }
}
