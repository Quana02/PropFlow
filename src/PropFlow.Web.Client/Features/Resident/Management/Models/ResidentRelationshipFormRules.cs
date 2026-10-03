using PropFlow.Web.Client.Features.Resident.Shared.Models;

namespace PropFlow.Web.Client.Features.Resident.Management.Models;

public sealed record ResidentRelationshipFormState(
    string RelationshipKind,
    Guid ApartmentUnitId,
    string ResidencyType,
    string HouseholdRole,
    string? HouseholdHeadResidencyId,
    string? RelationshipToHead);

public sealed record ResidentRelationshipBuildResult(
    CreateResidencyRequest? Residency,
    IReadOnlyDictionary<string, string> Errors);

public static class ResidentRelationshipFormRules
{
    public static bool HasCurrentOwnership(
        IEnumerable<OwnershipItem>? ownerships,
        Guid apartmentUnitId,
        DateOnly effectiveDate) =>
        ownerships?.Any(ownership =>
            ownership.ApartmentUnitId == apartmentUnitId &&
            ownership.StartDate <= effectiveDate &&
            ownership.EndDate is null) == true;

    public static ResidentRelationshipFormState ChangeRelationship(ResidentRelationshipFormState state, string relationshipKind)
    {
        if (relationshipKind == "OWNER_ONLY")
            return state with { RelationshipKind = relationshipKind, ResidencyType = "", HouseholdRole = "", HouseholdHeadResidencyId = null, RelationshipToHead = null };

        if (relationshipKind == "OWNER_AND_RESIDENT")
            return state with { RelationshipKind = relationshipKind, ResidencyType = "OWNER_OCCUPIED" };

        if (relationshipKind == "RESIDENT_ONLY")
            return state with
            {
                RelationshipKind = relationshipKind,
                ResidencyType = state.RelationshipKind == "RESIDENT_ONLY" ? state.ResidencyType : ""
            };

        return state with { RelationshipKind = relationshipKind, ResidencyType = "", HouseholdRole = "", HouseholdHeadResidencyId = null, RelationshipToHead = null };
    }

    public static ResidentRelationshipFormState ChangeHouseholdRole(ResidentRelationshipFormState state, string householdRole) =>
        householdRole == "HOUSEHOLD_MEMBER"
            ? state with { HouseholdRole = householdRole }
            : state with { HouseholdRole = householdRole, HouseholdHeadResidencyId = null, RelationshipToHead = null };

    public static ResidentRelationshipFormState ChangeApartment(ResidentRelationshipFormState state, Guid apartmentUnitId) =>
        state with { ApartmentUnitId = apartmentUnitId, HouseholdHeadResidencyId = null, RelationshipToHead = null };

    public static ResidentRelationshipFormState ReconcileHouseholdHeadAvailability(
        ResidentRelationshipFormState state,
        bool hasExistingHead) =>
        hasExistingHead && state.HouseholdRole == "HOUSEHOLD_HEAD"
            ? ChangeHouseholdRole(state, "")
            : state;

    public static ResidentRelationshipBuildResult Build(ResidentRelationshipFormState state)
    {
        if (state.RelationshipKind == "OWNER_ONLY") return new(null, new Dictionary<string, string>());
        var errors = new Dictionary<string, string>();
        var ownerAndResident = state.RelationshipKind == "OWNER_AND_RESIDENT";
        var residencyType = ownerAndResident ? "OWNER_OCCUPIED" : state.ResidencyType;
        var member = state.HouseholdRole == "HOUSEHOLD_MEMBER";

        if (!ownerAndResident && string.IsNullOrWhiteSpace(residencyType)) errors[nameof(state.ResidencyType)] = "Vui lòng chọn hình thức cư trú.";
        if (state.RelationshipKind == "RESIDENT_ONLY" && residencyType == "OWNER_OCCUPIED") errors[nameof(state.ResidencyType)] = "Quan hệ chỉ cư trú không được dùng hình thức chủ sở hữu đang cư trú.";
        if (string.IsNullOrWhiteSpace(state.HouseholdRole)) errors[nameof(state.HouseholdRole)] = "Vui lòng chọn vai trò trong hộ.";
        if (member && !Guid.TryParse(state.HouseholdHeadResidencyId, out _)) errors[nameof(state.HouseholdHeadResidencyId)] = "Vui lòng chọn chủ hộ.";
        if (member && string.IsNullOrWhiteSpace(state.RelationshipToHead)) errors[nameof(state.RelationshipToHead)] = "Vui lòng chọn quan hệ với chủ hộ.";

        var residency = new CreateResidencyRequest
        {
            ApartmentUnitId = state.ApartmentUnitId,
            HouseholdRole = state.HouseholdRole,
            ResidencyType = residencyType,
            HouseholdHeadResidencyId = member && Guid.TryParse(state.HouseholdHeadResidencyId, out var head) ? head : null,
            RelationshipToHead = member ? state.RelationshipToHead : null
        };
        return new(residency, errors);
    }
}
