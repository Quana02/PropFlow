using PropFlow.Modules.Apartments.Domain.ApartmentUnits;
using PropFlow.Web.Client.Features.Apartment.Models;

namespace PropFlow.UnitTests;

public sealed class ApartmentTypeDefaultsTests
{
    private static ApartmentUnitTypeResponse Type(decimal? area, int? bedrooms, int? bathrooms) => new(Guid.NewGuid(), "Mẫu", area, bedrooms, bathrooms);

    [Fact]
    public void Selection_copies_defaults_including_studio_zero_and_preserves_missing_values()
    {
        var original = new ApartmentMeasurements(76, 2, 2);
        Assert.Equal(new ApartmentMeasurements(35, 0, 1), ApartmentDefaultSelection.Apply(original, Type(35, 0, 1)));
        Assert.Equal(new ApartmentMeasurements(76, 2, 1), ApartmentDefaultSelection.Apply(original, Type(null, null, 1)));
        Assert.Equal(original, ApartmentDefaultSelection.Apply(original, Type(null, null, null)));
        Assert.Equal(new ApartmentMeasurements(null, null, null), ApartmentDefaultSelection.Apply(new(null, null, null), Type(null, null, null)));
    }

    [Fact]
    public void Changing_type_requires_confirmation_only_for_manual_values_that_would_be_overwritten()
    {
        var baseline = new ApartmentMeasurements(75, 2, 2);
        var manual = baseline with { Area = 76 };
        Assert.True(ApartmentDefaultSelection.NeedsConfirmation(manual, baseline, Type(78, 2, 2)));
        Assert.True(ApartmentDefaultSelection.NeedsConfirmation(baseline with { Area = null }, baseline, Type(78, 2, 2)));
        Assert.False(ApartmentDefaultSelection.NeedsConfirmation(baseline, baseline, Type(78, 3, 2)));
        Assert.False(ApartmentDefaultSelection.NeedsConfirmation(manual, baseline, Type(null, 3, 2)));
        Assert.False(ApartmentDefaultSelection.NeedsConfirmation(manual, baseline, Type(76, 2, 2)));
        // Preview/confirmation checks are pure; cancellation leaves the original values intact.
        Assert.Equal(76, manual.Area);
    }

    [Fact]
    public void Type_defaults_are_nullable_and_do_not_mutate_an_existing_apartment()
    {
        var now = DateTimeOffset.UtcNow;
        var type = new ApartmentUnitType("2 phòng ngủ", now, 75, 2, 2);
        var unit = new ApartmentUnit("A1203", 12, now, type.Id, 76, 2, 2);
        type.UpdateDefaults(78, 3, 2, now.AddMinutes(1));
        Assert.Equal(76, unit.UsableAreaM2);
        Assert.Equal(2, unit.BedroomCount);
        var legacy = new ApartmentUnitType("Loại cũ", now);
        Assert.Null(legacy.DefaultUsableAreaM2);
        Assert.Null(legacy.DefaultBedroomCount);
        Assert.Equal(0, new ApartmentUnitType("Studio", now, 35, 0, 1).DefaultBedroomCount);
    }

    [Theory]
    [InlineData("0", 1, 1)]
    [InlineData("-1", 1, 1)]
    [InlineData("24.99", 1, 1)]
    [InlineData("100000000", 1, 1)]
    [InlineData("35.001", 1, 1)]
    [InlineData("35", -1, 1)]
    [InlineData("35", 1, -1)]
    public void Invalid_defaults_are_rejected(string area, int bedrooms, int bathrooms) =>
        Assert.Throws<ArgumentException>(() => new ApartmentUnitType("Mẫu", DateTimeOffset.UtcNow,
            decimal.Parse(area, System.Globalization.CultureInfo.InvariantCulture), bedrooms, bathrooms));
}
