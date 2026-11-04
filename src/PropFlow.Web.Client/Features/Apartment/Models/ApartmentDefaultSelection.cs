namespace PropFlow.Web.Client.Features.Apartment.Models;

public sealed record ApartmentMeasurements(decimal? Area, int? Bedrooms, int? Bathrooms);

// Only fields supplied by the selected type are copied. Missing legacy values never become zero.
public static class ApartmentDefaultSelection
{
    public static ApartmentMeasurements Apply(ApartmentMeasurements current, ApartmentUnitTypeResponse type) =>
        new(type.DefaultUsableAreaM2 ?? current.Area, type.DefaultBedroomCount ?? current.Bedrooms, type.DefaultBathroomCount ?? current.Bathrooms);

    public static bool NeedsConfirmation(ApartmentMeasurements current, ApartmentMeasurements baseline, ApartmentUnitTypeResponse next) =>
        (next.DefaultUsableAreaM2.HasValue && current.Area != baseline.Area && current.Area != next.DefaultUsableAreaM2) ||
        (next.DefaultBedroomCount.HasValue && current.Bedrooms != baseline.Bedrooms && current.Bedrooms != next.DefaultBedroomCount) ||
        (next.DefaultBathroomCount.HasValue && current.Bathrooms != baseline.Bathrooms && current.Bathrooms != next.DefaultBathroomCount);
}
