using PropFlow.Web.Client.Shared.Forms;

namespace PropFlow.UnitTests;

public sealed class DateTextParserTests
{
    [Fact]
    public void Contract_UsesCanonicalFormatAndVietnameseError()
    {
        Assert.Equal("dd/MM/yyyy", PropFlowDateText.FormatPattern);
        Assert.Equal("Ngày không hợp lệ. Vui lòng nhập theo định dạng dd/MM/yyyy.", PropFlowDateText.InvalidMessage);
    }

    [Theory]
    [InlineData("02/02/2004", 2004, 2, 2)]
    [InlineData("31/05/2021", 2021, 5, 31)]
    [InlineData("31/05/2031", 2031, 5, 31)]
    public void TryParse_AcceptsStrictDayMonthYear(string text, int year, int month, int day)
    {
        Assert.True(PropFlowDateText.TryParse(text, out var value));
        Assert.Equal(new DateOnly(year, month, day), value);
        Assert.Equal(text, PropFlowDateText.Format(value));
    }

    [Theory]
    [InlineData("31/02/2024")]
    [InlineData("32/01/2024")]
    [InlineData("00/12/2024")]
    [InlineData("12/13/2024")]
    [InlineData("abc")]
    [InlineData("02-02-2004")]
    public void TryParse_RejectsInvalidOrNonCanonicalInput(string text)
    {
        Assert.False(PropFlowDateText.TryParse(text, out _));
    }

    [Theory]
    [InlineData("0", "", "0")]
    [InlineData("02", "0", "02/")]
    [InlineData("020", "02/", "02/0")]
    [InlineData("0202", "02/0", "02/02/")]
    [InlineData("02022004", "02/02/200", "02/02/2004")]
    [InlineData("02/02/2004", "", "02/02/2004")]
    [InlineData("02", "02/", "0")]
    [InlineData("02/02", "02/02/", "02/0")]
    public void NormalizeTyping_InsertsSeparatorsAndKeepsBackspaceUsable(string input, string previous, string expected)
    {
        Assert.Equal(expected, PropFlowDateText.NormalizeTyping(input, previous));
    }
}
