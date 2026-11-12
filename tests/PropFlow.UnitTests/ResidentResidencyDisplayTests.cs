using PropFlow.Web.Client.Features.Resident.Shared.Models;

namespace PropFlow.UnitTests;

[Trait("UseCase", "FE-02.2/FE-02.7")]
public sealed class ResidentResidencyDisplayTests
{
    [Theory]
    [InlineData("ACTIVE", -2, 2, true)]
    [InlineData("ACTIVE", 0, 0, true)]
    [InlineData("ACTIVE", -2, null, true)]
    [InlineData("ACTIVE", 1, null, false)]
    [InlineData("ACTIVE", -2, -1, false)]
    [InlineData("ENDED", -2, 2, false)]
    public void Current_residency_respects_status_and_inclusive_dates(string status, int startOffset, int? endOffset, bool expected)
    {
        var today = new DateOnly(2026, 11, 3);
        var row = new ResidencyItem(Guid.NewGuid(), Guid.NewGuid(), "T101", 1, "HOUSEHOLD_HEAD", "TENANT",
            null, null, null, null, today.AddDays(startOffset), endOffset.HasValue ? today.AddDays(endOffset.Value) : null, status, null);
        Assert.Equal(expected, row.IsActiveAt(today));
    }
}
