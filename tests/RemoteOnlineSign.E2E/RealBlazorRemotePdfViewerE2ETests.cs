using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Playwright;
using PdfSharpCore.Pdf;

namespace PropFlow.RemoteOnlineSign.E2E;

/// <summary>
/// Exercises the deployed Blazor WebAssembly shell.  API responses are mocked only at
/// the browser's HTTP boundary: the production client, AuthSession and authorization
/// components are never replaced or modified for this test.
/// </summary>
public sealed partial class RealBlazorRemotePdfViewerE2ETests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string repo = FindRepository();
    private static string FindRepository()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"PropFlow.sln")))return directory.FullName;
        throw new DirectoryNotFoundException("PropFlow repository root not found.");
    }
    private readonly string chrome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
    private readonly Guid contractId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid adminId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private Process? web; private Task<string>? hostStdout; private Task<string>? hostStderr; private IPlaywright? playwright; private IBrowser? browser; private IBrowserContext? context; private int port;
    private string? temporaryRoot;

    public async Task InitializeAsync()
    {
        if (!File.Exists(chrome)) throw new InvalidOperationException("Google Chrome is required for the Blazor E2E test.");
        port = Random.Shared.Next(51000, 58000);
        temporaryRoot = Path.Combine(Path.GetTempPath(), "propflow-remote-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var certificatePath = Path.Combine(temporaryRoot, "web-host.pfx");
        var keyRing = Path.Combine(temporaryRoot, "data-protection");
        Directory.CreateDirectory(keyRing);
        var certificatePassword = Guid.NewGuid().ToString("N");
        CreateCertificate(certificatePath, certificatePassword);
        var project = Path.Combine(repo, "src", "PropFlow.Web", "PropFlow.Web.csproj");
        var publishedWeb=Environment.GetEnvironmentVariable("PROPFLOW_REMOTE_E2E_WEB_DLL");
        var arguments=string.IsNullOrWhiteSpace(publishedWeb)
            ? $"run --no-build --no-launch-profile --project \"{project}\" --urls https://127.0.0.1:{port}"
            : $"\"{Path.GetFullPath(publishedWeb)}\" --urls https://127.0.0.1:{port}";
        var start = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(publishedWeb) ? repo : Path.GetDirectoryName(Path.GetFullPath(publishedWeb))!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["E2E__DataProtectionKeyRing"] = keyRing;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Path"] = certificatePath;
        start.Environment["ASPNETCORE_Kestrel__Certificates__Default__Password"] = certificatePassword;
        web = Process.Start(start) ?? throw new InvalidOperationException("Could not start the real PropFlow.Web host.");
        File.WriteAllText(Path.Combine(temporaryRoot,"owned-process.json"),JsonSerializer.Serialize(new {pid=web.Id,port,startedAt=web.StartTime}));
        hostStdout = web.StandardOutput.ReadToEndAsync();
        hostStderr = web.StandardError.ReadToEndAsync();
        await WaitForHostAsync();
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = chrome });
        context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
    }

    [Fact]
    public async Task Real_blazor_contract_detail_renders_drag_resize_and_read_only_fields()
    {
        var page = await context!.NewPageAsync();
        var diagnostics = new List<string>();
        var network = new List<string>();
        page.PageError += (_, error) => diagnostics.Add("pageerror: " + error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                diagnostics.Add($"console: {message.Text}; location={message.Location}");
        };
        page.RequestFailed += (_, request) => network.Add($"failed method={request.Method} type={request.ResourceType} url={request.Url} failure={request.Failure}" );
        page.Response += (_, response) =>
        {
            var path = new Uri(response.Url).AbsolutePath;
            if (response.Status >= 400 || path is "/_framework/blazor.web.js" or "/_framework/dotnet.js" or "/_framework/blazor.boot.json" || path.Contains("pdf", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/v1/", StringComparison.Ordinal))
                network.Add($"response status={response.Status} method={response.Request.Method} type={response.Request.ResourceType} url={response.Url}");
        };
        await page.AddInitScriptAsync("window.__remotePdfTelemetry={stage:'bootstrap',events:[]};");
        await InstallApiBoundaryAsync(page);

        // App.razor intentionally includes third-party presentation resources.  Their
        // availability is not a prerequisite for the WASM feature, so network-idle
        // would make this browser test depend on unrelated external connections.
        await page.GotoAsync($"https://127.0.0.1:{port}/remote-contracts/{contractId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        try { await page.Locator(".remote-pdf-stage canvas").First.WaitForAsync(new() { Timeout = 15_000 }); }
        catch (TimeoutException)
        {
            var timeoutArtifact = Path.Combine(repo, "tests", "RemoteOnlineSign.E2E", "artifacts", "real-blazor-canvas-timeout.png");
            Directory.CreateDirectory(Path.GetDirectoryName(timeoutArtifact)!);
            await page.ScreenshotAsync(new() { Path = timeoutArtifact, FullPage = true });
            var dom = await page.EvaluateAsync<string>("() => document.body.innerText.slice(0, 4000)");
            var stage = await page.Locator(".remote-pdf-stage").CountAsync();
            throw new Xunit.Sdk.XunitException($"Canvas did not render. url={page.Url}; stageCount={stage}; body={dom}; network={string.Join(" | ", network)}; diagnostics={string.Join(" | ", diagnostics)}");
        }
        var field = page.Locator(".remote-pdf-field.resident");
        await field.WaitForAsync();
        await field.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(100);
        var before = await field.BoundingBoxAsync(); Assert.NotNull(before);

        var box = before!;
        var startX = box.X + Math.Min(20, box.Width / 2); var startY = box.Y + Math.Min(20, box.Height / 2);
        if (!double.IsFinite(startX) || !double.IsFinite(startY))
            throw new Xunit.Sdk.XunitException($"Drag start is non-finite. box=({box.X},{box.Y},{box.Width},{box.Height}); start=({startX},{startY})");
        var coordinatePayload = JsonSerializer.Serialize(new { x = (double)startX, y = (double)startY }, Json);
        await page.EvaluateAsync("""payload => { const raw=JSON.parse(payload); const x=raw.x, y=raw.y; const field=document.querySelector('.remote-pdf-field.resident'); const snapshot=()=>{const r=field?.getBoundingClientRect(),s=field?getComputedStyle(field):null,p=field?.offsetParent?.getBoundingClientRect();return r?{x:r.x,y:r.y,width:r.width,height:r.height,inline:{left:field.style.left,top:field.style.top},computed:{position:s.position,left:s.left,top:s.top,transform:s.transform,display:s.display,contain:s.contain},offsetParent:field.offsetParent?.className??null,parent:p?{x:p.x,y:p.y,width:p.width,height:p.height}:null}:null}; const viewport={width:window.innerWidth,height:window.innerHeight}; const finite=Number.isFinite(x)&&Number.isFinite(y); const inViewport=finite&&x>=0&&x<viewport.width&&y>=0&&y<viewport.height; const hit=inViewport?document.elementFromPoint(x,y):null; window.__remoteDragDiagnostic={events:[],start:{x,y,finite,inViewport,viewport,raw,hit:hit?.className??hit?.tagName,editable:field?.classList.contains('editable'),pointerEvents:field?getComputedStyle(field).pointerEvents:null,zIndex:field?getComputedStyle(field).zIndex:null,layout:snapshot()}}; if(!field||!finite||!inViewport)return; for(const type of ['pointerdown','pointermove','pointerup','lostpointercapture']) field.addEventListener(type,e=>window.__remoteDragDiagnostic.events.push({type,target:e.target.className,current:e.currentTarget.className,x:e.clientX,y:e.clientY,capture:field.hasPointerCapture(e.pointerId),layout:snapshot()}),true); }""", coordinatePayload);
        await page.Mouse.MoveAsync(startX, startY); await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(startX + 38, startY + 20, new() { Steps = 5 }); await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(150);
        var afterDrag = await field.BoundingBoxAsync(); Assert.NotNull(afterDrag);
        if (afterDrag!.X <= before.X + 10)
        {
            var dragArtifact = Path.Combine(repo, "tests", "RemoteOnlineSign.E2E", "artifacts", "real-blazor-drag-timeout.png");
            Directory.CreateDirectory(Path.GetDirectoryName(dragArtifact)!); await page.ScreenshotAsync(new() { Path = dragArtifact, FullPage = true });
            var telemetry = await page.EvaluateAsync<string>("JSON.stringify(window.__remoteDragDiagnostic)");
            var viewerTelemetry = await page.EvaluateAsync<string>("JSON.stringify(window.__remotePdfTelemetry?.events ?? [])");
            throw new Xunit.Sdk.XunitException($"field did not move: {before.X} -> {afterDrag.X}; start=({startX},{startY}); telemetry={telemetry}; viewerTelemetry={viewerTelemetry}; network={string.Join(" | ", network)}; diagnostics={string.Join(" | ", diagnostics)}");
        }

        var handle = field.Locator(".resize"); var resizeBefore = await field.BoundingBoxAsync(); var handleBox = await handle.BoundingBoxAsync();
        Assert.NotNull(handleBox);
        await page.Mouse.MoveAsync(handleBox!.X + 3, handleBox.Y + 3); await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(handleBox.X + 32, handleBox.Y + 24, new() { Steps = 5 }); await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(150);
        var resizeAfter = await field.BoundingBoxAsync(); Assert.NotNull(resizeAfter);
        Assert.True(resizeAfter!.Width > resizeBefore!.Width + 8, $"field did not resize: {resizeBefore.Width} -> {resizeAfter.Width}");

        var artifact = Path.Combine(repo, "tests", "RemoteOnlineSign.E2E", "artifacts", "real-blazor-drag-resize.png");
        Directory.CreateDirectory(Path.GetDirectoryName(artifact)!); await page.ScreenshotAsync(new() { Path = artifact, FullPage = true });

        // Switching to a sent contract happens only in the HTTP mock; the real page
        // still re-renders through its production client and must remove edit affordances.
        await page.ReloadAsync();
        await page.RouteAsync("**/api/v1/remote-online-sign/11111111-1111-1111-1111-111111111111", route => route.FulfillAsync(JsonResponse(new { id = contractId, creatorId = adminId, residentId = Guid.Parse("33333333-3333-3333-3333-333333333333"), status = "PENDING_INVESTOR_SIGNATURE", signingMode = "INVESTOR_AND_RESIDENT", deadline = (DateTimeOffset?)null, revision = 1 })));
        await page.ReloadAsync(); await page.Locator(".remote-pdf-stage canvas").First.WaitForAsync(new() { Timeout = 15_000 });
        Assert.Equal(0, await page.Locator(".remote-pdf-field .resize").CountAsync());
        Assert.True(diagnostics.Count == 0,
            "Browser diagnostics:\n" + string.Join("\n", diagnostics) +
            "\nRelevant network trace:\n" + string.Join("\n", network));
    }

    private async Task InstallApiBoundaryAsync(IPage page)
    {
        var pdf = Pdf();
        await page.RouteAsync("**/api/v1/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path.EndsWith("/auth/csrf", StringComparison.Ordinal))
                await route.FulfillAsync(JsonResponse(new { token = "e2e-csrf-token" }));
            else if (path is "/api/v1/residents/me" or "/api/v1/resident/weather" or "/api/v1/emergency-calls/active")
                await route.FulfillAsync(new() { Status = 204 });
            else if (path.EndsWith("/auth/refresh", StringComparison.Ordinal))
            {
                await route.FulfillAsync(JsonResponse(new { accessToken = "a.eyJzaWQiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDAifQ.c", expiresAt = DateTimeOffset.UtcNow.AddMinutes(20), user = new { id = adminId, username = "e2e-admin", displayName = "E2E Admin", email = "admin@example.test", phoneNumber = (string?)null, status = "ACTIVE", emailVerified = true, role = "ADMIN", permissions = Array.Empty<string>(), avatarVersion = (Guid?)null } }));
            }
            else if (path.EndsWith("/notifications/unread-count", StringComparison.Ordinal)) await route.FulfillAsync(JsonResponse(new { count = 0 }));
            else if (path.EndsWith("/notifications", StringComparison.Ordinal)) await route.FulfillAsync(JsonResponse(Array.Empty<object>()));
            else if (path.EndsWith("/history", StringComparison.Ordinal)) await route.FulfillAsync(JsonResponse(Array.Empty<object>()));
            else if (path.EndsWith("/document/metadata", StringComparison.Ordinal)) await route.FulfillAsync(JsonResponse(new { id = Guid.Parse("44444444-4444-4444-4444-444444444444"), sha256 = "a".PadLeft(64, 'a'), size = pdf.Length, pageCount = 2, pageMetadata = "[]", isFinal = false }));
            else if (path.EndsWith("/signature-fields", StringComparison.Ordinal)) await route.FulfillAsync(JsonResponse(new[] { new { id = Guid.Parse("55555555-5555-5555-5555-555555555555"), role = "RESIDENT", page = 1, x = 72m, y = 96m, width = 180m, height = 60m } }));
            else if (path.EndsWith("/document", StringComparison.Ordinal)) await route.FulfillAsync(new() { Status = 200, ContentType = "application/pdf", BodyBytes = pdf });
            else if (path.EndsWith($"/remote-online-sign/{contractId}", StringComparison.Ordinal)) await route.FulfillAsync(JsonResponse(new { id = contractId, creatorId = adminId, residentId = Guid.Parse("33333333-3333-3333-3333-333333333333"), status = "DRAFT", signingMode = "INVESTOR_AND_RESIDENT", deadline = (DateTimeOffset?)null, revision = 1 }));
            else await route.FulfillAsync(new() { Status = 404, ContentType = "application/problem+json", Body = "{\"title\":\"unexpected E2E request\"}" });
        });
        // Register after the generic API route: Playwright evaluates matching routes
        // in reverse registration order. Regex matching keeps the dynamic HTTPS port
        // and negotiateVersion query from changing the test boundary.
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/hubs/notifications(?:/negotiate)?(?:\?.*)?$"), route =>
        {
            var uri = new Uri(route.Request.Url);
            if (uri.AbsolutePath.EndsWith("/negotiate", StringComparison.Ordinal) && route.Request.Method == "POST")
                return route.FulfillAsync(JsonResponse(new { connectionId = "e2e-notification", connectionToken = "e2e-notification", availableTransports = new[] { new { transport = "LongPolling", transferFormats = new[] { "Text" } } } }));
            if (route.Request.Method == "POST")
                return route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = "{}\u001e" });
            if (route.Request.Method == "GET")
                return route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = "\u001e" });
            // SignalR Long Polling terminates the connection with DELETE during page
            // reload/disposal.  The transport expects an accepted successful response.
            if (route.Request.Method == "DELETE")
                return route.FulfillAsync(new() { Status = 202 });
            return route.FulfillAsync(new() { Status = 405, ContentType = "application/problem+json", Body = "{\"title\":\"unexpected hub method\"}" });
        });
        var emergencyHandshakeReceived = false;
        var emergencyHandshakeDelivered = false;
        var emergencyHubTrace = new List<string>();
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/hubs/emergency-calls(?:/negotiate)?(?:\?.*)?$"), route =>
        {
            var uri = new Uri(route.Request.Url);
            emergencyHubTrace.Add($"{route.Request.Method} {uri.PathAndQuery}");
            if (uri.AbsolutePath.EndsWith("/negotiate", StringComparison.Ordinal) && route.Request.Method == "POST")
                return route.FulfillAsync(JsonResponse(new { connectionId = "e2e-emergency-calls", connectionToken = "e2e-emergency-calls", availableTransports = new[] { new { transport = "LongPolling", transferFormats = new[] { "Text" } } } }));
            if (route.Request.Method == "POST")
            {
                emergencyHandshakeReceived = true;
                // Long Polling POST is a send acknowledgement.  The server handshake
                // response is delivered by the receive GET, never by this response.
                return route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = string.Empty });
            }
            if (route.Request.Method == "GET")
            {
                if (emergencyHandshakeReceived && !emergencyHandshakeDelivered)
                {
                    emergencyHandshakeDelivered = true;
                    return route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = "{}\u001e" });
                }
                // An empty poll is not a SignalR frame; a standalone record separator
                // would be parsed as an invalid, empty JSON handshake response.
                return route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = string.Empty });
            }
            if (route.Request.Method == "DELETE") return route.FulfillAsync(new() { Status = 202 });
            return route.FulfillAsync(new() { Status = 405, ContentType = "application/problem+json", Body = "{\"title\":\"unexpected emergency hub method\"}" });
        });
    }

    private static RouteFulfillOptions JsonResponse(object value) => new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(value, Json) };
    private static byte[] Pdf() { using var pdf = new PdfDocument(); var a = pdf.AddPage(); a.Width = 595; a.Height = 842; var b = pdf.AddPage(); b.Width = 842; b.Height = 595; b.Rotate = 90; using var output = new MemoryStream(); pdf.Save(output, false); return output.ToArray(); }
    private async Task WaitForHostAsync()
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        handler.UseProxy = false;
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var url = $"https://127.0.0.1:{port}/__e2e/ready";
        var deadline = Stopwatch.StartNew(); Exception? last = null; var attempts = 0;
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            attempts++;
            if (web?.HasExited == true) break;
            using var requestTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                using var response = await client.GetAsync(url, requestTimeout.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    await EnsureWasmAssetsAsync(client);
                    return;
                }
                last = new HttpRequestException($"Readiness returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }
            catch (OperationCanceledException exception) when (requestTimeout.IsCancellationRequested) { last = exception; }
            catch (HttpRequestException exception) { last = exception; }
            await Task.Delay(250);
        }
        var exit = web?.HasExited == true ? web.ExitCode.ToString() : "still running";
        var output = hostStdout is { IsCompletedSuccessfully: true } ? hostStdout.Result : "(host stdout still streaming)";
        var error = hostStderr is { IsCompletedSuccessfully: true } ? hostStderr.Result : "(host stderr still streaming)";
        throw new InvalidOperationException($"Host readiness failed. url={url}; elapsed={deadline.Elapsed}; attempts={attempts}; hostExit={exit}; last={last?.GetBaseException().Message}; stdout={output[^Math.Min(2000, output.Length)..]}; stderr={error[^Math.Min(2000, error.Length)..]}");
    }
    private async Task EnsureWasmAssetsAsync(HttpClient client)
    {
        foreach (var path in new[] { "/_framework/blazor.web.js", "/_framework/dotnet.js", "/PropFlow.Web.styles.css", "/js/theme.js", "/css/fe041.css" })
        {
            using var response = await client.GetAsync($"https://127.0.0.1:{port}{path}");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"WASM static asset readiness failed: {path} returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }
    }
    private static void CreateCertificate(string path, string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
    }
    public async Task DisposeAsync()
    {
        if (context is not null) await context.DisposeAsync(); if (browser is not null) await browser.DisposeAsync(); playwright?.Dispose();
        if (web is { HasExited: false }) { web.Kill(entireProcessTree: true); await web.WaitForExitAsync(); }
        if (hostStdout is not null) await hostStdout; if (hostStderr is not null) await hostStderr;
        web?.Dispose();
        if (temporaryRoot is not null) { try { Directory.Delete(temporaryRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
