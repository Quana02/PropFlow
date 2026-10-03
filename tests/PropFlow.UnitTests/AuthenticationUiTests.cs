namespace PropFlow.UnitTests;

public sealed class AuthenticationUiTests
{
    [Fact]
    public void LoginValidation_UsesDismissibleToastInsteadOfInlineSummary()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionDirectory(),
            "src",
            "PropFlow.Web.Client",
            "Features",
            "Authentication",
            "Pages",
            "Login.razor"));

        Assert.Contains("OnInvalidSubmit=\"HandleInvalidSubmit\"", source, StringComparison.Ordinal);
        Assert.Contains("<PropFlowToast", source, StringComparison.Ordinal);
        Assert.Contains("DurationMs=\"4000\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ValidationSummary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ApiFeedback", source, StringComparison.Ordinal);
    }

    private static string FindSolutionDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PropFlow.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Không tìm thấy PropFlow.sln từ thư mục kiểm thử.");
    }
}
