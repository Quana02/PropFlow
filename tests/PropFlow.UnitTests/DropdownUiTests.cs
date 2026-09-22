namespace PropFlow.UnitTests;

public sealed class DropdownUiTests
{
    private static readonly string SolutionDirectory = FindSolutionDirectory();

    [Fact]
    public void WebClient_UsesSharedSelectInsteadOfNativeSelectFields()
    {
        var client = Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client");
        var razorFiles = Directory.EnumerateFiles(client, "*.razor", SearchOption.AllDirectories);

        foreach (var file in razorFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("<select", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<InputSelect", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SharedSelect_DeclaresKeyboardAndListboxAccessibility()
    {
        var source = File.ReadAllText(Path.Combine(SolutionDirectory, "src", "PropFlow.Web.Client", "Shared", "Forms", "PropFlowSelect.razor"));

        Assert.Contains("aria-expanded", source);
        Assert.Contains("aria-controls", source);
        Assert.Contains("aria-activedescendant", source);
        Assert.Contains("role=\"listbox\"", source);
        Assert.Contains("role=\"option\"", source);
        Assert.Contains("ArrowDown", source);
        Assert.Contains("ArrowUp", source);
        Assert.Contains("Escape", source);
    }

    private static string FindSolutionDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PropFlow.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Không tìm thấy PropFlow.sln từ thư mục kiểm thử.");
    }
}
