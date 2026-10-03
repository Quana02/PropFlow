using PropFlow.Web.Client.Features.Resident.Management.Models;
using PropFlow.Web.Client.Features.Resident.Shared.Models;

namespace PropFlow.UnitTests;

public sealed class ResidentRelationshipFormRulesTests
{
    private static readonly Guid ApartmentA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ApartmentB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Head = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void OwnerOnly_ClearsEveryResidencyField_AndBuildsNoResidency()
    {
        var member = new ResidentRelationshipFormState("RESIDENT_ONLY", ApartmentA, "TENANT", "HOUSEHOLD_MEMBER", Head.ToString(), "CHILD");

        var state = ResidentRelationshipFormRules.ChangeRelationship(member, "OWNER_ONLY");
        var result = ResidentRelationshipFormRules.Build(state);

        Assert.Equal("", state.ResidencyType);
        Assert.Equal("", state.HouseholdRole);
        Assert.Null(state.HouseholdHeadResidencyId);
        Assert.Null(state.RelationshipToHead);
        Assert.Null(result.Residency);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("RESIDENT_ONLY", "TENANT")]
    [InlineData("RESIDENT_ONLY", "AUTHORIZED_OCCUPANT")]
    [InlineData("OWNER_AND_RESIDENT", "OWNER_OCCUPIED")]
    public void HouseholdHead_BuildsValidPayloadWithoutMemberFields(string relationshipKind, string expectedResidencyType)
    {
        var state = new ResidentRelationshipFormState(relationshipKind, ApartmentA, expectedResidencyType, "HOUSEHOLD_HEAD", Head.ToString(), "CHILD");

        var result = ResidentRelationshipFormRules.Build(state);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Residency);
        Assert.Equal(expectedResidencyType, result.Residency.ResidencyType);
        Assert.Equal("HOUSEHOLD_HEAD", result.Residency.HouseholdRole);
        Assert.Null(result.Residency.HouseholdHeadResidencyId);
        Assert.Null(result.Residency.RelationshipToHead);
    }

    [Theory]
    [InlineData("RESIDENT_ONLY", "TENANT")]
    [InlineData("RESIDENT_ONLY", "AUTHORIZED_OCCUPANT")]
    [InlineData("OWNER_AND_RESIDENT", "OWNER_OCCUPIED")]
    public void HouseholdMember_RequiresBothMemberFields_ThenBuildsThem(string relationshipKind, string expectedResidencyType)
    {
        var missing = new ResidentRelationshipFormState(relationshipKind, ApartmentA, expectedResidencyType, "HOUSEHOLD_MEMBER", null, null);
        var invalid = ResidentRelationshipFormRules.Build(missing);
        Assert.Equal(2, invalid.Errors.Count);
        Assert.Contains(nameof(ResidentRelationshipFormState.HouseholdHeadResidencyId), invalid.Errors.Keys);
        Assert.Contains(nameof(ResidentRelationshipFormState.RelationshipToHead), invalid.Errors.Keys);

        var valid = ResidentRelationshipFormRules.Build(missing with { HouseholdHeadResidencyId = Head.ToString(), RelationshipToHead = "CHILD" });
        Assert.Empty(valid.Errors);
        Assert.Equal(expectedResidencyType, valid.Residency!.ResidencyType);
        Assert.Equal(Head, valid.Residency.HouseholdHeadResidencyId);
        Assert.Equal("CHILD", valid.Residency.RelationshipToHead);
    }

    [Fact]
    public void MemberToHead_ClearsMemberFields_AndHeadToMemberRequiresThemAgain()
    {
        var member = new ResidentRelationshipFormState("RESIDENT_ONLY", ApartmentA, "TENANT", "HOUSEHOLD_MEMBER", Head.ToString(), "CHILD");
        var head = ResidentRelationshipFormRules.ChangeHouseholdRole(member, "HOUSEHOLD_HEAD");
        Assert.Null(head.HouseholdHeadResidencyId);
        Assert.Null(head.RelationshipToHead);
        Assert.Empty(ResidentRelationshipFormRules.Build(head).Errors);

        var memberAgain = ResidentRelationshipFormRules.ChangeHouseholdRole(head, "HOUSEHOLD_MEMBER");
        Assert.Equal(2, ResidentRelationshipFormRules.Build(memberAgain).Errors.Count);
    }

    [Fact]
    public void RelationshipTransitions_DeriveOrClearResidencyTypeWithoutStaleValues()
    {
        var owner = new ResidentRelationshipFormState("OWNER_ONLY", ApartmentA, "", "", null, null);
        var resident = ResidentRelationshipFormRules.ChangeRelationship(owner, "RESIDENT_ONLY");
        Assert.Equal("", resident.ResidencyType);

        resident = resident with { ResidencyType = "TENANT", HouseholdRole = "HOUSEHOLD_HEAD" };
        var ownerResident = ResidentRelationshipFormRules.ChangeRelationship(resident, "OWNER_AND_RESIDENT");
        Assert.Equal("OWNER_OCCUPIED", ownerResident.ResidencyType);
        Assert.Equal("HOUSEHOLD_HEAD", ownerResident.HouseholdRole);

        var residentAgain = ResidentRelationshipFormRules.ChangeRelationship(ownerResident, "RESIDENT_ONLY");
        Assert.Equal("", residentAgain.ResidencyType);
        Assert.Equal("HOUSEHOLD_HEAD", residentAgain.HouseholdRole);
    }

    [Fact]
    public void ResidentOnly_RejectsOwnerOccupiedEvenWhenSubmittedFromStaleClientState()
    {
        var state = new ResidentRelationshipFormState(
            "RESIDENT_ONLY",
            ApartmentA,
            "OWNER_OCCUPIED",
            "HOUSEHOLD_HEAD",
            null,
            null);

        var result = ResidentRelationshipFormRules.Build(state);

        Assert.Contains(nameof(ResidentRelationshipFormState.ResidencyType), result.Errors.Keys);
    }

    [Fact]
    public void OwnerOccupiedAvailability_RequiresCurrentOwnershipOfTheSelectedApartment()
    {
        var ownerships = new[]
        {
            new OwnershipItem(Guid.NewGuid(), ApartmentA, "P101", 1, new DateOnly(2026, 1, 1), null),
            new OwnershipItem(Guid.NewGuid(), ApartmentB, "P202", 2, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))
        };

        Assert.True(ResidentRelationshipFormRules.HasCurrentOwnership(ownerships, ApartmentA, new DateOnly(2026, 10, 3)));
        Assert.False(ResidentRelationshipFormRules.HasCurrentOwnership(ownerships, ApartmentB, new DateOnly(2026, 10, 3)));
        Assert.False(ResidentRelationshipFormRules.HasCurrentOwnership(ownerships, ApartmentA, new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void ApartmentChange_ClearsHeadAndRelationship_ButPreservesRoleAndType()
    {
        var member = new ResidentRelationshipFormState("RESIDENT_ONLY", ApartmentA, "TENANT", "HOUSEHOLD_MEMBER", Head.ToString(), "CHILD");

        var changed = ResidentRelationshipFormRules.ChangeApartment(member, ApartmentB);

        Assert.Equal(ApartmentB, changed.ApartmentUnitId);
        Assert.Equal("TENANT", changed.ResidencyType);
        Assert.Equal("HOUSEHOLD_MEMBER", changed.HouseholdRole);
        Assert.Null(changed.HouseholdHeadResidencyId);
        Assert.Null(changed.RelationshipToHead);
    }

    [Fact]
    public void ExistingHouseholdHead_ClearsAHeadSelectionForTheSelectedApartment()
    {
        var selectedHead = new ResidentRelationshipFormState("RESIDENT_ONLY", ApartmentA, "TENANT", "HOUSEHOLD_HEAD", null, null);

        var reconciled = ResidentRelationshipFormRules.ReconcileHouseholdHeadAvailability(selectedHead, hasExistingHead: true);

        Assert.Equal("", reconciled.HouseholdRole);
        Assert.Null(reconciled.HouseholdHeadResidencyId);
        Assert.Null(reconciled.RelationshipToHead);
    }
}
