using System.Net;
using System.Text;
using Microsoft.Playwright;
using PdfSharpCore.Pdf;

namespace PropFlow.RemoteOnlineSign.E2E;

public sealed class RemotePdfViewerE2ETests : IAsyncLifetime
{
    private readonly string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private readonly string chrome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
    private HttpListener? server; private Task? loop; private int port; private IPlaywright? playwright; private IBrowser? browser; private readonly List<string> serverDiagnostics = [];

    public async Task InitializeAsync()
    {
        if (!File.Exists(chrome)) throw new InvalidOperationException("Google Chrome binary is not available for the Remote E2E harness.");
        port = Random.Shared.Next(42000, 49000); server = new HttpListener(); server.Prefixes.Add($"http://127.0.0.1:{port}/"); server.Start(); loop = Task.Run(ServeAsync);
        playwright = await Playwright.CreateAsync(); browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, ExecutablePath = chrome });
    }

    [Fact]
    public async Task Viewer_import_and_canvas_render_is_stable_over_five_runs()
    {
        for (var run = 1; run <= 5; run++)
        {
            var page = await browser!.NewPageAsync(); var errors = new List<string>(); var responses = new List<string>(); var failures = new List<string>();
            page.PageError += (_, error) => errors.Add("pageerror: " + error); page.Console += (_, message) => { if (message.Type == "error") errors.Add("console: " + message.Text); };
            page.RequestFailed += (_, request) => failures.Add($"{request.Method} {request.ResourceType} {request.Url} :: {request.Failure}"); page.Response += (_, response) => { if (response.Status >= 400 || response.Url.Contains("pdf.mjs") || response.Url.Contains("pdf.worker.mjs") || response.Url.Contains("fixture.pdf") || response.Url.Contains("remote-pdf")) responses.Add($"{response.Request.Method} {response.Request.ResourceType} {response.Status} {response.Url} {response.Headers.GetValueOrDefault("content-type", "missing")}"); };
            await page.GotoAsync($"http://127.0.0.1:{port}/harness.html"); await WaitForRenderAsync(page, errors, responses, failures);
            Assert.NotNull(await page.Locator(".remote-pdf-stage canvas").BoundingBoxAsync()); Assert.Empty(errors); Assert.Empty(failures); Assert.DoesNotContain(responses, response => response.Contains(" 404 "));
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task Viewer_renders_pages_zoom_rotation_drag_resize_and_read_only_overlay()
    {
        var page = await browser!.NewPageAsync(); var consoleErrors = new List<string>(); var responses = new List<string>(); var failures = new List<string>();
        page.PageError += (_, error) => consoleErrors.Add("pageerror: " + error); page.Console += (_, message) => { if (message.Type == "error") consoleErrors.Add("console: " + message.Text); };
        page.RequestFailed += (_, request) => failures.Add($"{request.Method} {request.ResourceType} {request.Url} :: {request.Failure}"); page.Response += (_, response) => { if (response.Status >= 400 || response.Url.Contains("pdf.mjs") || response.Url.Contains("pdf.worker.mjs") || response.Url.Contains("fixture.pdf") || response.Url.Contains("remote-pdf")) responses.Add($"{response.Request.Method} {response.Request.ResourceType} {response.Status} {response.Url} {response.Headers.GetValueOrDefault("content-type", "missing")}"); };
        await page.GotoAsync($"http://127.0.0.1:{port}/harness.html"); await WaitForRenderAsync(page, consoleErrors, responses, failures);
        Assert.Equal("2", await page.Locator("#pageCount").TextContentAsync());
        var initial = await page.Locator(".remote-pdf-field").BoundingBoxAsync(); Assert.NotNull(initial);
        foreach (var zoom in new[] { "0.5", "1", "1.5", "2" }) { await page.EvaluateAsync($"renderPage(1,{zoom},0,true)"); await WaitForRenderAsync(page, consoleErrors, responses, failures); }
        foreach (var rotation in new[] { 0, 90, 180, 270 }) { await page.EvaluateAsync($"renderPage(1,1,{rotation},true)"); await WaitForRenderAsync(page, consoleErrors, responses, failures); Assert.NotNull(await page.Locator(".remote-pdf-field").BoundingBoxAsync()); }
        await page.EvaluateAsync("renderPage(1,1,0,true)"); var field = page.Locator(".remote-pdf-field");
        await page.EvaluateAsync("""() => { const field=document.querySelector('.remote-pdf-field'); window.__dragTelemetry={down:0,move:0,up:0,capture:false,calls:0,callbackError:null,computed:getComputedStyle(field).pointerEvents,zIndex:getComputedStyle(field).zIndex}; for(const type of ['pointerdown','pointermove','pointerup'])field.addEventListener(type, e=>{window.__dragTelemetry[type.slice(7)]++;if(type==='pointerdown'){try{window.__dragTelemetry.capture=field.hasPointerCapture(e.pointerId)}catch{}}},true); const original=window.renderPage; }""");
        var box = (await field.BoundingBoxAsync())!; await page.Mouse.MoveAsync(box.X + 8, box.Y + 8); await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(box.X + 48, box.Y + 30, new() { Steps = 5 }); await page.Mouse.UpAsync();
        var dragTelemetry = await page.EvaluateAsync<string>("JSON.stringify(window.__dragTelemetry)");
        try { await page.WaitForFunctionAsync("document.querySelector('#lastX').textContent !== '72'", null, new() { Timeout = 2_000 }); }
        catch (TimeoutException) { throw new Xunit.Sdk.XunitException("Drag callback did not update. telemetry=" + dragTelemetry + "; lastX=" + await page.Locator("#lastX").TextContentAsync()); }
        Assert.NotEqual("72", await page.Locator("#lastX").TextContentAsync());
        var handle = page.Locator(".remote-pdf-field .resize"); var h = (await handle.BoundingBoxAsync())!; await page.Mouse.MoveAsync(h.X + 2, h.Y + 2); await page.Mouse.DownAsync(); await page.Mouse.MoveAsync(h.X + 35, h.Y + 25); await page.Mouse.UpAsync(); await page.WaitForFunctionAsync("document.querySelector('#lastWidth').textContent !== '180'", null, new() { Timeout = 2_000 }); Assert.NotEqual("180", await page.Locator("#lastWidth").TextContentAsync());
        var artifactDirectory = Path.Combine(repo, "tests", "RemoteOnlineSign.E2E", "artifacts"); Directory.CreateDirectory(artifactDirectory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(artifactDirectory, "viewer.png") });
        await page.EvaluateAsync("renderPage(1,1,0,false)"); Assert.Equal(0, await page.Locator(".remote-pdf-field .resize").CountAsync()); Assert.Empty(consoleErrors);
    }

    public async Task DisposeAsync() { if (browser is not null) await browser.DisposeAsync(); playwright?.Dispose(); if (server is not null) { server.Stop(); server.Close(); } if (loop is not null) await loop; }
    private async Task WaitForRenderAsync(IPage page, List<string> errors, List<string> responses, List<string> failures)
    {
        try { await page.WaitForFunctionAsync("window.__viewerDiagnostic && ['done','failed'].includes(window.__viewerDiagnostic.stage)", null, new() { Timeout = 7_000 }); }
        catch (TimeoutException) { }
        var diagnostic = await page.EvaluateAsync<string>("JSON.stringify(window.__viewerDiagnostic)");
        if (!string.Equals(await page.EvaluateAsync<string>("window.__viewerDiagnostic.stage"), "done", StringComparison.Ordinal))
            throw new Xunit.Sdk.XunitException($"PDF viewer did not render. diagnostic={diagnostic}; responses={string.Join(" | ", responses)}; failures={string.Join(" | ", failures)}; errors={string.Join(" | ", errors)}; server={string.Join(" | ", serverDiagnostics)}");
    }
    private async Task ServeAsync()
    {
        while (server!.IsListening) try { var context = await server.GetContextAsync(); _ = Task.Run(() => ServeContextAsync(context)); } catch (HttpListenerException) { break; }
    }
    private async Task ServeContextAsync(HttpListenerContext context)
    {
        try { var path = context.Request.Url!.AbsolutePath.TrimStart('/'); if (path == "harness.html") await SendAsync(context, Harness(), "text/html"); else if (path == "fixture.pdf") await SendAsync(context, Pdf(), "application/pdf"); else { var absolute = Path.Combine(repo, "src", "PropFlow.Web.Client", "wwwroot", path.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(absolute)) { context.Response.StatusCode = 404; await SendAsync(context, Encoding.UTF8.GetBytes("not found: " + path), "text/plain"); } else await SendAsync(context, await File.ReadAllBytesAsync(absolute), path.EndsWith(".mjs") || path.EndsWith(".js") ? "text/javascript" : "application/octet-stream"); } } catch (Exception exception) { lock (serverDiagnostics) serverDiagnostics.Add($"{context.Request.HttpMethod} {context.Request.Url}: {exception.GetType().Name}: {exception.Message}"); if (context.Response.OutputStream.CanWrite) context.Response.Close(); }
    }
    private static async Task SendAsync(HttpListenerContext context, string text, string contentType) => await SendAsync(context, Encoding.UTF8.GetBytes(text), contentType);
    private static async Task SendAsync(HttpListenerContext context, byte[] bytes, string contentType) { context.Response.ContentType = contentType; context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close(); }
    private static byte[] Pdf() { using var pdf = new PdfDocument(); var first = pdf.AddPage(); first.Width = 595; first.Height = 842; var second = pdf.AddPage(); second.Width = 842; second.Height = 595; second.Rotate = 90; using var stream = new MemoryStream(); pdf.Save(stream, false); return stream.ToArray(); }
    private static string Harness() => """<!doctype html><link rel='icon' href='data:,'><style>.remote-pdf-stage{position:relative;margin:auto}.remote-pdf-stage canvas{display:block}.remote-pdf-field{position:absolute;z-index:1;box-sizing:border-box;border:2px solid #2563eb;background:#dbeafeaa;padding:3px;user-select:none}.remote-pdf-field.editable{cursor:move}.remote-pdf-field .resize{position:absolute;right:-4px;bottom:-4px;width:10px;height:10px;background:#2563eb;cursor:nwse-resize}</style><div id='host'></div><output id='pageCount'>2</output><output id='lastX'>72</output><output id='lastWidth'>180</output><script>window.__viewerDiagnostic={stage:'bootstrap',errors:[],events:[]};window.__remotePdfTelemetry=window.__viewerDiagnostic;addEventListener('error',e=>window.__viewerDiagnostic.errors.push('error:'+e.message));addEventListener('unhandledrejection',e=>window.__viewerDiagnostic.errors.push('rejection:'+String(e.reason)));</script><script type='module'>try{window.__viewerDiagnostic.stage='module-import-start';const viewer=await import('/Features/RemoteOnlineSign/Pages/remote-pdf-viewer.js');window.__viewerDiagnostic.stage='module-import-complete';const host=document.querySelector('#host'),lastX=document.querySelector('#lastX'),lastWidth=document.querySelector('#lastWidth'),field={role:'RESIDENT',page:1,x:72,y:96,width:180,height:60};const dotnet={invokeMethodAsync:(_,__,___,x,y,w,h)=>{lastX.textContent=Math.round(x);lastWidth.textContent=Math.round(w)}};window.__viewerDiagnostic.stage='fixture';const bytes=new Uint8Array(await (await fetch('/fixture.pdf')).arrayBuffer());window.renderPage=async(p,z,r,e)=>{try{await viewer.render(host,bytes,p,z,r,[field],e,dotnet);window.__viewerDiagnostic.stage='done'}catch(err){window.__viewerDiagnostic.stage='failed';window.__viewerDiagnostic.errors.push(String(err));}};await renderPage(1,1,0,true)}catch(err){window.__viewerDiagnostic.stage='failed';window.__viewerDiagnostic.errors.push(String(err))}</script>""";
}
