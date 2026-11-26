using Microsoft.Playwright;

namespace PropFlow.RemoteOnlineSign.E2E;

public sealed partial class RealBlazorRemotePdfViewerE2ETests
{
    [Fact]
    public async Task Resident_picker_displays_loading_empty_error_and_recovers_after_search_failure()
    {
        var setup = await NewPickerPageAsync(); var page = setup.Page; var diagnostics = setup.Diagnostics;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); var errorRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected503Responses = new List<string>();
        var candidate = new { userId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), residentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), fullName = "Recovery Resident", residentCode = "RS-REC", apartments = Array.Empty<string>() };
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/api/v1/remote-online-sign/resident-candidates(?:\?.*)?$"), async route =>
        {
            var term = System.Web.HttpUtility.ParseQueryString(new Uri(route.Request.Url).Query)["search"] ?? "";
            requested.TrySetResult(term);
            if (term == "empty") { await pending.Task; await route.FulfillAsync(JsonResponse(Array.Empty<object>())); return; }
            if (term == "error") { errorRequested.TrySetResult(); await route.FulfillAsync(new() { Status = 503, ContentType = "application/problem+json", Body = "{\"title\":\"search unavailable\"}" }); return; }
            await route.FulfillAsync(JsonResponse(new[] { candidate }));
        });
        page.Response += (_, response) =>
        {
            var uri = new Uri(response.Url);
            if (response.Status == 503 && response.Request.Method == "GET" && uri.AbsolutePath.EndsWith("/resident-candidates", StringComparison.Ordinal) && System.Web.HttpUtility.ParseQueryString(uri.Query)["search"] == "error")
                expected503Responses.Add(response.Url);
        };
        var input = page.Locator("input[placeholder='Tên, mã cư dân hoặc email']");
        await input.FillAsync("empty"); await requested.Task;
        await page.GetByText("Đang tìm cư dân…", new() { Exact = true }).WaitForAsync();
        pending.TrySetResult(); await page.GetByText("Không tìm thấy cư dân đủ điều kiện ký.", new() { Exact = true }).WaitForAsync();
        await input.FillAsync("error"); await errorRequested.Task; await page.GetByText("search unavailable", new() { Exact = false }).WaitForAsync();
        await input.FillAsync("recover"); await page.GetByText("Recovery Resident", new() { Exact = false }).WaitForAsync();
        await input.FillAsync(""); await page.WaitForFunctionAsync("() => document.querySelectorAll('.remote-resident-candidates button').length === 0");
        Assert.Single(expected503Responses);
        var unexpectedDiagnostics = diagnostics.Where(message => !message.Contains("Failed to load resource", StringComparison.OrdinalIgnoreCase) || expected503Responses.Count != 1).ToArray();
        Assert.True(unexpectedDiagnostics.Length == 0, "Unexpected browser diagnostics:\n" + string.Join("\n", unexpectedDiagnostics) + "\nExpected 503:\n" + string.Join("\n", expected503Responses));
    }

    [Fact]
    public async Task Resident_picker_debounces_input_and_does_not_render_stale_search_results()
    {
        var setup = await NewPickerPageAsync(); var page = setup.Page; var diagnostics = setup.Diagnostics;
        var requests = new List<string>(); var oldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var oldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldCandidate = new { userId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010"), residentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000010"), fullName = "Old Resident", residentCode = "RS-OLD", apartments = Array.Empty<string>() };
        var newCandidate = new { userId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011"), residentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000011"), fullName = "New Resident", residentCode = "RS-NEW", apartments = Array.Empty<string>() };
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/api/v1/remote-online-sign/resident-candidates(?:\?.*)?$"), async route =>
        {
            var term = System.Web.HttpUtility.ParseQueryString(new Uri(route.Request.Url).Query)["search"] ?? ""; requests.Add(term);
            if (term == "old") { oldStarted.TrySetResult(); try { await oldGate.Task; await route.FulfillAsync(JsonResponse(new[] { oldCandidate })); } catch { } return; }
            await route.FulfillAsync(JsonResponse(new[] { newCandidate }));
        });
        var input = page.Locator("input[placeholder='Tên, mã cư dân hoặc email']");
        await input.FillAsync("o"); await input.FillAsync("ol"); await input.FillAsync("old");
        await oldStarted.Task;
        await input.FillAsync("new"); await page.GetByText("New Resident", new() { Exact = false }).WaitForAsync();
        oldGate.TrySetResult();
        await page.WaitForFunctionAsync("() => document.body.innerText.includes('New Resident')");
        Assert.DoesNotContain("Old Resident", await page.Locator("body").InnerTextAsync());
        Assert.DoesNotContain("o", requests.Where(x => x is "o" or "ol")); Assert.Contains("new", requests);
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
    }

    private async Task<(IPage Page, List<string> Diagnostics)> NewPickerPageAsync()
    {
        var diagnostics = new List<string>(); var page = await context!.NewPageAsync(); page.PageError += (_, error) => diagnostics.Add("pageerror: " + error); page.Console += (_, message) => { if (message.Type == "error") diagnostics.Add("console: " + message.Text); };
        await page.AddInitScriptAsync("window.__remotePdfTelemetry={stage:'bootstrap',events:[]};"); await InstallApiBoundaryAsync(page);
        await page.GotoAsync($"https://127.0.0.1:{port}/remote-contracts/{contractId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator(".remote-pdf-stage canvas").WaitForAsync(new() { Timeout = 15_000 }); return (page, diagnostics);
    }
}
