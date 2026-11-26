namespace PropFlow.Web.Client.Features.Apartment.Models;

public sealed record ApartmentDirectoryMultiFilter(
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> FloorNumbers,
    IReadOnlyList<string> ApartmentUnitTypeIds)
{
    public static ApartmentDirectoryMultiFilter Empty { get; } = new([], [], []);
    public int ActiveCount => (Statuses.Count > 0 ? 1 : 0) + (FloorNumbers.Count > 0 ? 1 : 0)
        + (ApartmentUnitTypeIds.Count > 0 ? 1 : 0);
    public string Query => string.Concat(new[] { ("statuses", Statuses), ("floorNumbers", FloorNumbers),
        ("apartmentUnitTypeIds", ApartmentUnitTypeIds) }
        .SelectMany(group => group.Item2.Distinct().Select(value => "&" + group.Item1 + "=" + Uri.EscapeDataString(value))));
}
