using PropFlow.Web.Client.Features.Apartment.Models;

namespace PropFlow.UnitTests;

public sealed class ApartmentStatusActionUxTests
{
    [Theory]
    [InlineData(true, false, "Căn hộ vẫn còn chủ sở hữu hiện hành. Vui lòng kết thúc toàn bộ quyền sở hữu trước khi ngừng quản lý căn hộ.")]
    [InlineData(false, true, "Căn hộ vẫn còn cư dân đang cư trú. Vui lòng kết thúc các quan hệ cư trú hiện hành trước khi ngừng quản lý căn hộ.")]
    [InlineData(true, true, "Căn hộ vẫn còn chủ sở hữu và cư dân đang hoạt động. Hãy kết thúc các quyền sở hữu và quan hệ cư trú hiện hành trước khi ngừng quản lý.")]
    public void KnownState_ReturnsSpecificBlockerMessage(bool hasOwners, bool hasResidents, string expected)
    {
        Assert.Equal(expected, ApartmentStatusActionUx.BlockerMessage(hasOwners, hasResidents));
    }

    [Fact]
    public void VacantApartment_HasNoBlockerMessage()
    {
        Assert.Null(ApartmentStatusActionUx.BlockerMessage(false, false));
    }

    [Theory]
    [InlineData("APARTMENT_HAS_CURRENT_OWNERS", "chủ sở hữu hiện hành")]
    [InlineData("APARTMENT_HAS_ACTIVE_RESIDENTS", "cư dân đang cư trú")]
    [InlineData("APARTMENT_HAS_CURRENT_OWNERS_AND_ACTIVE_RESIDENTS", "chủ sở hữu và cư dân")]
    public void BackendConflict_MapsToSpecificBlockerDialog(string code, string expectedFragment)
    {
        Assert.True(ApartmentStatusActionUx.TryMapConflict(code, out var message));
        Assert.Contains(expectedFragment, message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownBackendError_IsNotTreatedAsBusinessBlocker()
    {
        Assert.False(ApartmentStatusActionUx.TryMapConflict("unexpected", out _));
    }

    [Fact]
    public void StopActions_RemainClickableAndUseSharedDialog()
    {
        var list = File.ReadAllText(Source("Features", "Apartment", "Pages", "ApartmentManagement.razor"));
        var detail = File.ReadAllText(Source("Features", "Apartment", "Pages", "ApartmentDetail.razor"));

        Assert.DoesNotContain("disabled=\"@CannotDeactivate", list, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled=\"@CannotDeactivate", detail, StringComparison.Ordinal);
        Assert.Contains("ApartmentStatusActionUx.BlockerMessage", list, StringComparison.Ordinal);
        Assert.Contains("ApartmentStatusActionUx.BlockerMessage", detail, StringComparison.Ordinal);
        Assert.Contains("<PropFlowConfirmationDialog", list, StringComparison.Ordinal);
        Assert.Contains("<PropFlowConfirmationDialog", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ApartmentDetail_UsesBalancedDesktopColumns()
    {
        var styles = File.ReadAllText(Source("Features", "Apartment", "Pages", "ApartmentDetail.razor.css"));

        Assert.Contains(
            ".detail-layout{display:grid;grid-template-columns:repeat(2,minmax(0,1fr))",
            styles,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ApartmentDetail_ShowsTheCompleteResidencyTimelineInTheLeftColumn()
    {
        var source = File.ReadAllText(Source("Features", "Apartment", "Pages", "ApartmentDetail.razor"));

        Assert.Contains("<div class=\"main-stack\">", source, StringComparison.Ordinal);
        Assert.Contains("Lịch sử cư trú (@ResidencyTimeline.Count)", source, StringComparison.Ordinal);
        Assert.Contains("@foreach(var resident in ResidencyTimeline)", source, StringComparison.Ordinal);
        Assert.Contains("apartment.CurrentResidents.Concat(apartment.ResidentHistory)", source, StringComparison.Ordinal);
        Assert.Contains("</section></div><aside class=\"side-stack\">", source, StringComparison.Ordinal);
    }

    private static string Source(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PropFlow.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, "src", "PropFlow.Web.Client", .. segments]);
    }
}
