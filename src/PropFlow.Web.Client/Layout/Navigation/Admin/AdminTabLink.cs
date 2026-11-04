using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Rendering;
using PropFlow.Web.Client.Features.Administration;

namespace PropFlow.Web.Client.Layout.Navigation.Admin;

// Each link observes navigation independently, including query-only changes and browser history.
public sealed class AdminTabLink : ComponentBase, IDisposable
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Parameter] public string Tab { get; set; } = AdminDashboardTabs.Overview;
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
    private bool isCurrent;

    protected override void OnInitialized() => Navigation.LocationChanged += LocationChanged;
    protected override void OnParametersSet() => isCurrent = MatchesUri(Navigation.Uri, Tab);

    private void LocationChanged(object? sender, LocationChangedEventArgs args)
    {
        var next = MatchesUri(args.Location, Tab);
        if (next == isCurrent) return;
        isCurrent = next;
        _ = InvokeAsync(StateHasChanged);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "a");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        var css = AdditionalAttributes?.TryGetValue("class", out var value) == true ? value?.ToString() : null;
        builder.AddAttribute(2, "class", isCurrent ? $"{css} is-selected".Trim() : css);
        builder.AddAttribute(3, "aria-current", isCurrent ? "page" : null);
        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }

    public static bool MatchesUri(string currentUriAbsolute, string tab)
    {
        if (!Uri.TryCreate(currentUriAbsolute, UriKind.Absolute, out var uri)
            || !uri.AbsolutePath.TrimEnd('/').Equals("/admin/dashboard", StringComparison.OrdinalIgnoreCase)) return false;
        var parameter = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.StartsWith("tab=", StringComparison.OrdinalIgnoreCase));
        var requested = parameter is null ? null : Uri.UnescapeDataString(parameter[4..].Replace('+', ' '));
        return AdminDashboardTabs.Normalize(requested) == tab;
    }

    public void Dispose() => Navigation.LocationChanged -= LocationChanged;
}

