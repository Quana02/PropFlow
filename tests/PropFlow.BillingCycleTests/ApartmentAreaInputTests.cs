using PropFlow.Web.Client.Features.Apartment.Models;

namespace PropFlow.BillingCycleTests;
public sealed class ApartmentAreaInputTests
{
    [Theory]
    [InlineData("25,5", "25.5")]
    [InlineData("25.5", "25.5")]
    [InlineData("0,02", "0.02")]
    [InlineData(" 70 ", "70")]
    public void Area_input_reads_decimal_without_thousands_conversion(string text, string expected) =>
        Assert.Equal(decimal.Parse(expected,System.Globalization.CultureInfo.InvariantCulture),ApartmentAreaInput.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("25,5.6")]
    [InlineData("2e2")]
    public void Invalid_area_clears_previous_numeric_value(string text) => Assert.Null(ApartmentAreaInput.Parse(text));
}
