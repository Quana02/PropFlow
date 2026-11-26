using System.Text.Json;
using Microsoft.Playwright;

namespace PropFlow.RemoteOnlineSign.E2E;

public sealed partial class RealBlazorRemotePdfViewerE2ETests
{
    [Fact]
    public async Task Resident_picker_search_select_assign_and_reload_preserves_assigned_resident()
    {
        var page = await context!.NewPageAsync();
        var diagnostics = new List<string>(); var network = new List<string>(); var remoteRequests = new List<string>(); var candidateRequests = new List<string>(); var assigned = false; Guid? assignedUserId = null;
        var residentUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var residentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        page.PageError += (_, error) => diagnostics.Add("pageerror: " + error);
        page.Console += (_, message) => { if (message.Type == "error") diagnostics.Add("console: " + message.Text); };
        page.Request += (_, request) => { var path = new Uri(request.Url).AbsolutePath; if (path.StartsWith("/api/v1/remote-online-sign", StringComparison.Ordinal)) remoteRequests.Add($"request {request.Method} {request.Url}"); if (path.EndsWith("/resident-candidates", StringComparison.Ordinal)) candidateRequests.Add($"request {request.Method} {request.Url}"); };
        page.Response += (_, response) => { if (response.Status >= 400 || new Uri(response.Url).AbsolutePath.EndsWith("/resident-candidates", StringComparison.Ordinal)) network.Add($"response {response.Status} {response.Request.Method} {response.Url}"); };
        page.RequestFailed += (_, request) => network.Add($"failed {request.Method} {request.Url}: {request.Failure}");
        await page.AddInitScriptAsync("window.__remotePdfTelemetry={stage:'bootstrap',events:[]};");
        await InstallApiBoundaryAsync(page);
        var candidate = new { userId = residentUserId, residentId = residentId, fullName = "Nguyen Van An", residentCode = "RS-001", apartments = new[] { "A-101" } };
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/api/v1/remote-online-sign/resident-candidates(?:\?.*)?$"), route =>
        {
            Assert.Equal("GET", route.Request.Method);
            Assert.Contains("search=Nguyen", route.Request.Url, StringComparison.Ordinal);
            return route.FulfillAsync(JsonResponse(new[] { candidate }));
        });
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}/resident", async route =>
        {
            Assert.Equal("PUT", route.Request.Method);
            using var payload = JsonDocument.Parse(route.Request.PostData ?? "{}");
            assignedUserId = payload.RootElement.GetProperty("residentUserId").GetGuid(); assigned = true;
            await route.FulfillAsync(JsonResponse(new { id = contractId, creatorId = adminId, residentId = residentUserId, residentName = "Nguyen Van An", residentCode = "RS-001", status = "DRAFT", signingMode = "INVESTOR_AND_RESIDENT", deadline = (DateTimeOffset?)null, revision = 1 }));
        });
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}", route => route.FulfillAsync(JsonResponse(new { id = contractId, creatorId = adminId, residentId = assigned ? residentUserId : (Guid?)null, residentName = assigned ? "Nguyen Van An" : null, residentCode = assigned ? "RS-001" : null, status = "DRAFT", signingMode = "INVESTOR_AND_RESIDENT", deadline = (DateTimeOffset?)null, revision = 1 })));
        await page.GotoAsync($"https://127.0.0.1:{port}/remote-contracts/{contractId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        // DOMContentLoaded only proves prerendered markup is available. The existing
        // real-viewer test uses canvas creation as the InteractiveWebAssembly-ready
        // condition, so wait for the same production lifecycle before dispatching input.
        await page.Locator(".remote-pdf-stage canvas").WaitForAsync(new() { Timeout = 15_000 });
        var search = page.Locator("input[placeholder='Tên, mã cư dân hoặc email']");
        await search.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await page.EvaluateAsync("""() => { const input=document.querySelector("input[placeholder='Tên, mã cư dân hoặc email']"); window.__residentPickerEvents=[]; for(const type of ['focus','input','change','blur']) input?.addEventListener(type, e => window.__residentPickerEvents.push({type,value:e.target.value})); }""");
        await search.FillAsync("Nguyen");
        try { await page.Locator(".remote-resident-candidates button").WaitForAsync(new() { Timeout = 5_000 }); }
        catch (TimeoutException)
        {
            var body = await page.Locator("body").InnerTextAsync();
            var value = await search.InputValueAsync();
            var picker = await page.Locator(".remote-card").AllInnerTextsAsync();
            var candidateButtonCount = await page.Locator(".remote-resident-candidates button").CountAsync();
            var events = await page.EvaluateAsync<string>("JSON.stringify(window.__residentPickerEvents ?? [])");
            throw new Xunit.Sdk.XunitException($"Resident candidates did not render. url={page.Url}; input={value}; candidateButtons={candidateButtonCount}; events={events}; remoteRequests={string.Join(" | ", remoteRequests)}; requests={string.Join(" | ", candidateRequests)}; network={string.Join(" | ", network)}; picker={string.Join(" | ", picker)}; body={body[..Math.Min(2000, body.Length)]}; diagnostics={string.Join(" | ", diagnostics)}; mock={JsonSerializer.Serialize(candidate)}");
        }
        await page.GetByText("Nguyen Van An", new() { Exact = false }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Gán cư dân", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.body.innerText.includes('Đã gán cư dân.')");
        Assert.Equal(residentUserId, assignedUserId); Assert.NotEqual(residentId, assignedUserId);
        await page.ReloadAsync();
        await page.GetByText("Nguyen Van An", new() { Exact = false }).WaitForAsync();
        Assert.Contains("RS-001", await page.Locator("body").InnerTextAsync());
        Assert.True(diagnostics.Count == 0, "Browser diagnostics:\n" + string.Join("\n", diagnostics));
    }
}
