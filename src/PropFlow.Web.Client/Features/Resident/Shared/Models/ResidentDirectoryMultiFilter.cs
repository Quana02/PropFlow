namespace PropFlow.Web.Client.Features.Resident.Shared.Models;

// The same snapshot is sent by list pagination and Excel export.
public sealed record ResidentDirectoryMultiFilter(
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> ApartmentUnitIds,
    IReadOnlyList<string> FloorNumbers,
    IReadOnlyList<string> RelationshipKinds)
{
    public static ResidentDirectoryMultiFilter Empty { get; } = new([], [], [], []);
    public int ActiveCount => (Statuses.Count > 0 ? 1 : 0) + (ApartmentUnitIds.Count > 0 ? 1 : 0)
        + (FloorNumbers.Count > 0 ? 1 : 0) + (RelationshipKinds.Count > 0 ? 1 : 0);
    public string Query => string.Concat(new[] { ("statuses", Statuses), ("apartmentUnitIds", ApartmentUnitIds),
        ("floorNumbers", FloorNumbers), ("relationshipKinds", RelationshipKinds) }
        .SelectMany(group => group.Item2.Distinct().Select(value => "&" + group.Item1 + "=" + Uri.EscapeDataString(value))));
}
