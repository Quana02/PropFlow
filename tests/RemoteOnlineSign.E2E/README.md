# Remote Online Sign Blazor E2E

Run this browser test from a normal Windows PowerShell session, not a restricted Codex sandbox. It starts `PropFlow.Web` on a random HTTPS loopback port, creates a short-lived self-signed certificate and Data Protection key ring in `%TEMP%`, and deletes both during cleanup. It does not trust the certificate, use a database, or call Cloudinary/SMTP.

```powershell
Set-Location 'D:\0-File đồ án tốt nghiệp-new\PropFlow'
dotnet msbuild src\PropFlow.Web.Client\PropFlow.Web.Client.csproj /t:Build /m:1 /p:Restore=false
dotnet msbuild src\PropFlow.Web\PropFlow.Web.csproj /t:Build /m:1 /p:Restore=false
dotnet test tests\RemoteOnlineSign.E2E\PropFlow.RemoteOnlineSign.E2E.csproj --no-restore --filter 'FullyQualifiedName~RealBlazorRemotePdfViewerE2ETests'
```

Install Playwright browsers only if the local test cannot use Chrome:

```powershell
pwsh tests\RemoteOnlineSign.E2E\bin\Debug\net8.0\playwright.ps1 install chromium
```

The test mocks only browser requests matching `/api/v1/**`. Requests to `_framework`, PDF.js, CSS, and other static assets remain served by the real Blazor host. A failure before `GET /__e2e/ready` is test-host infrastructure; a failure after the Blazor shell loads is investigated as client/viewer behavior. Screenshots, if reached, are in `tests\RemoteOnlineSign.E2E\artifacts`.
