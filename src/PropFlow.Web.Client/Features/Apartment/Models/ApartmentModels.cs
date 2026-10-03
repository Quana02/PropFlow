namespace PropFlow.Web.Client.Features.Apartment.Models;

public sealed record ApartmentSummary(int TotalCount, int OccupiedCount, int VacantCount, int InactiveCount);
public sealed record PagedApartmentsResponse(IReadOnlyList<ApartmentListItem> Items, int TotalCount, int PageIndex, int PageSize, ApartmentSummary Summary);
public sealed record ApartmentListItem(Guid Id, string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, string UnitType, decimal UsableAreaM2, string Status, IReadOnlyList<ResidentLookupItem> Owners, bool IsOccupied);
public sealed record ApartmentUnitTypeResponse(Guid Id, string Name);
public sealed record ResidentLookupItem(Guid ResidentId, string ResidentCode, string FullName);
public sealed record ActiveResidentAssociation(Guid ResidentId, string ResidentCode, string FullName, string HouseholdRole, string ResidencyType, string? RelationshipToHead, DateOnly StartDate, string ResidencyStatus);
public sealed record ResidentAssociationItem(Guid ResidencyId, Guid ResidentId, string ResidentCode, string FullName, string HouseholdRole, string ResidencyType, Guid? HouseholdHeadResidencyId, string? HouseholdHeadName, string? HouseholdHeadResidentCode, string? RelationshipToHead, DateOnly StartDate, DateOnly? EndDate, string ResidencyStatus)
{
    public string? HouseholdHeadFullName => HouseholdHeadName;
    public string Status => ResidencyStatus;
}
public sealed record ApartmentResponse(Guid Id, string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, string UnitType, decimal UsableAreaM2, int? BedroomCount, int? BathroomCount, DateOnly? HandoverDate, string? Description, string Status, IReadOnlyList<ApartmentOwnerItem> Owners, IReadOnlyList<ApartmentOwnershipHistoryItem> OwnershipHistory, IReadOnlyList<ResidentAssociationItem> CurrentResidents, IReadOnlyList<ResidentAssociationItem> ResidentHistory, IReadOnlyList<ActiveResidentAssociation> Occupants);
public sealed record ResidentApartmentSelfItem(Guid Id, string UnitNumber, int FloorNumber, Guid ApartmentUnitTypeId, string UnitType, decimal UsableAreaM2, int? BedroomCount, int? BathroomCount, DateOnly? HandoverDate, string Status);
public sealed record ApartmentOwnerItem(Guid OwnershipId, Guid ResidentId, ResidentLookupItem? Owner, DateOnly StartDate, DateOnly? EndDate);
public sealed record ApartmentOwnershipCandidate(Guid Id, string UnitNumber, int FloorNumber, string UnitType, string Status);
public sealed record ApartmentOwnershipHistoryItem(Guid OwnershipId, Guid OwnerResidentId, ResidentLookupItem? Owner, DateOnly StartDate, DateOnly? EndDate);

public class CreateApartmentRequest
{
    public string UnitNumber { get; set; } = "";
    public int FloorNumber { get; set; }
    public Guid ApartmentUnitTypeId { get; set; }
    public decimal UsableAreaM2 { get; set; }
    public int? BedroomCount { get; set; }
    public int? BathroomCount { get; set; }
    public DateOnly? HandoverDate { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "ACTIVE";
}

public sealed class UpdateApartmentRequest : CreateApartmentRequest { }
public sealed class CreateApartmentUnitTypeRequest { public string Name { get; set; } = ""; }
public sealed class UpdateApartmentUnitTypeRequest { public string Name { get; set; } = ""; }
public sealed class ChangeApartmentOwnerRequest { public Guid OwnerResidentId { get; set; } public DateOnly? EffectiveDate { get; set; } }
public sealed class EndApartmentOwnershipRequest { public DateOnly? EndDate { get; set; } }
public sealed class SetApartmentStatusRequest { public string Status { get; set; } = "ACTIVE"; }
