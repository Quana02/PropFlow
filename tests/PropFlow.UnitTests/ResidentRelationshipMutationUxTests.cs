namespace PropFlow.UnitTests;

public sealed class ResidentRelationshipMutationUxTests
{
    [Fact]
    public void Detail_supports_multiple_current_residencies_and_atomic_move()
    {
        var source = File.ReadAllText(Source("Features", "Resident", "Management", "Pages", "ResidentDetail.razor"));

        Assert.Contains("@foreach(var item in ActiveResidencies)", source, StringComparison.Ordinal);
        Assert.Contains("ResidencyCandidatesAsync(Id)", source, StringComparison.Ordinal);
        Assert.Contains("MoveResidencyAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EndResidencyAsync(Id,old.Id", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Quan hệ cũ đã kết thúc nhưng chưa tạo được quan hệ mới", source, StringComparison.Ordinal);
    }

    [Fact]
    public void All_relationship_mutations_require_shared_confirmation()
    {
        var source = File.ReadAllText(Source("Features", "Resident", "Management", "Pages", "ResidentDetail.razor"));

        Assert.True(source.Split("<PropFlowConfirmationDialog", StringSplitOptions.None).Length - 1 >= 5);
        Assert.Contains("ConfirmResidencyMutation", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmEndResidency", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmAddOwnership", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmEndOwnership", source, StringComparison.Ordinal);
        Assert.Contains("InformationOnly=\"true\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_ownership_apartment_icon_aligns_with_the_title()
    {
        var styles = File.ReadAllText(Source("Features", "Resident", "Management", "Pages", "ResidentDetail.razor.css"));

        Assert.Contains(".ownership-card .compact-item>.item-icon{align-self:start", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Resident_status_feedback_uses_auto_dismissing_toast_instead_of_inline_banner()
    {
        var source = File.ReadAllText(Source("Features", "Resident", "Management", "Pages", "ResidentDetail.razor"));

        Assert.DoesNotContain("resident-notice @(actionError?\"error\":\"success\")", source, StringComparison.Ordinal);
        Assert.Contains("ShowToast(\"Đã cập nhật trạng thái cư dân.\")", source, StringComparison.Ordinal);
        Assert.Contains("DurationMs=\"@toastDuration\"", source, StringComparison.Ordinal);
    }

    private static string Source(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PropFlow.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, "src", "PropFlow.Web.Client", .. segments]);
    }
}
