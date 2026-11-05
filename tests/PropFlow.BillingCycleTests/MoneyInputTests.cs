using PropFlow.Web.Client.Shared.Forms;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Features.Finance.Components;
using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace PropFlow.BillingCycleTests;

public sealed class MoneyInputTests
{
    [Theory]
    [InlineData("4.000", "4.0000", "40000")]
    [InlineData("40.000", "40.0000", "400000")]
    [InlineData("4.000.000", "4.000.00", "400000")]
    [InlineData("4.000.000", "40.000.000", "40000000")]
    [InlineData("4.000.000", "", "")]
    [InlineData("4.000.000", "400000000", "400000000")]
    [InlineData("4.000.000", "400.000.000", "400000000")]
    [InlineData("4.000", "10.5", "10.5")]
    [InlineData("10.5", "10.50", "10.50")]
    public void Digit_edits_regroup_existing_separators_without_reinterpreting_pasted_decimals(string previous, string text, string expected)
        => Assert.Equal(expected, PropFlowMoneyText.NormalizeEdit(previous, text));

    [Theory]
    [InlineData("4000000", "4.000.000")]
    [InlineData("400000000", "400.000.000")]
    public async Task Typing_each_digit_through_the_field_keeps_the_full_amount(string digits, string expected)
    {
        var field = new FinanceNumberField();
        decimal? amount = null;
        ParameterView.FromDictionary(new Dictionary<string, object?> {
            ["Label"] = "Đơn giá", ["Max"] = 999999999999999999m,
            ["ValueChanged"] = EventCallback.Factory.Create<decimal?>(this, value => amount = value)
        }).SetParameterProperties(field);
        var change = typeof(FinanceNumberField).GetMethod("Change", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var display = typeof(FinanceNumberField).GetProperty("DisplayValue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var text = "";
        foreach (var digit in digits)
        {
            await (Task)change.Invoke(field, [text + digit])!;
            text = (string)display.GetValue(field)!;
            Assert.NotNull(amount);
        }
        Assert.Equal(expected, text);
        Assert.Equal(decimal.Parse(digits), amount);
    }

    [Theory]
    [InlineData("10000", "10.000", "10000")]
    [InlineData("100000", "100.000", "100000")]
    [InlineData("1.000.000", "1.000.000", "1000000")]
    [InlineData("999999999999999999", "999.999.999.999.999.999", "999999999999999999")]
    [InlineData("0", "0", "0")]
    public void Money_groups_digits_and_round_trips_to_exact_decimal(string input, string display, string expected)
    {
        Assert.Equal(display, PropFlowMoneyText.FormatInput(input));
        Assert.Null(FinanceInputValidation.Number(PropFlowMoneyText.Normalize(display), "Số tiền", 0, 999999999999999999m, 0, out var value));
        Assert.Equal(decimal.Parse(expected), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("10.5")]
    [InlineData("10,5")]
    [InlineData("1..000")]
    [InlineData("1.00.000")]
    public void Invalid_money_is_not_silently_changed_into_another_amount(string input)
    {
        Assert.NotNull(FinanceInputValidation.Number(PropFlowMoneyText.Normalize(input), "Số tiền", 0, 999999999999999999m, 0, out var value));
        Assert.Null(value);
    }
}

