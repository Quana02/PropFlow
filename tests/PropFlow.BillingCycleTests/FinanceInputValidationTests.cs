using PropFlow.Web.Client.Features.Finance.Services;

namespace PropFlow.BillingCycleTests;
public sealed class FinanceInputValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("12 000")]
    [InlineData("1.234.567")]
    [InlineData("9999999999999999999999999999999999")]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("1.0001")]
    public void Kwh_rejects_missing_invalid_or_out_of_range_input(string? text)
    {
        var error = FinanceInputValidation.Number(text, "Điện đã dùng (kWh)", 0, 1000000, 3, out var value);
        Assert.False(string.IsNullOrWhiteSpace(error)); Assert.Null(value);
    }
    [Theory]
    [InlineData("0", 0)]
    [InlineData("123.456", 123.456)]
    [InlineData("123,456", 123.456)]
    [InlineData("1000000", 1000000)]
    public void Kwh_accepts_zero_and_both_decimal_separators(string text, decimal expected)
    {
        Assert.Null(FinanceInputValidation.Number(text, "Điện đã dùng (kWh)", 0, 1000000, 3, out var value));
        Assert.Equal(expected, value);
    }
    [Theory]
    [InlineData("0")]
    [InlineData("-3000")]
    [InlineData("3.000")]
    [InlineData("3,000")]
    [InlineData("3000.5")]
    [InlineData("1000001")]
    public void Electricity_price_requires_positive_whole_vnd_without_grouping(string text)
    {
        Assert.NotNull(FinanceInputValidation.Number(text, "Điện VND/kWh", 1, 1000000, 0, out var value));
        Assert.Null(value);
    }
    [Fact]
    public void Default_electricity_price_is_accepted_and_empty_input_never_falls_back_to_previous_value()
    {
        Assert.Null(FinanceInputValidation.Number("3000", "Điện VND/kWh", 1, 1000000, 0, out var value));
        Assert.Equal(3000, value);
        Assert.NotNull(FinanceInputValidation.Number("", "Điện VND/kWh", 1, 1000000, 0, out value));
        Assert.Null(value);
    }
}
