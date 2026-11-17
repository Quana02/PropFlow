param(
    [ValidateSet('Full', 'Routes', 'Api', 'Session', 'Maintenance', 'Theme', 'Proxy')]
    [string]$Scope = 'Full',
    [switch]$UseRelease,
    [ValidateRange(1, 200)][int]$LoadUsers = 10,
    [switch]$ChromeSession,
    [switch]$ChromeNotifications
)
$ErrorActionPreference = 'Stop'
$auditRoot = (Get-Location).Path
$auditArtifacts = Join-Path $auditRoot 'artifacts/authz'
$auditFixture = Join-Path $auditArtifacts 'fixture-build/bin/BrowserFixture/debug/BrowserFixture.dll'
$auditDatabase = 'propflow_phase11_' + [guid]::NewGuid().ToString('N')
. (Join-Path $PSScriptRoot 'test-database.ps1')
$auditProcesses = @()
$auditDatabaseStarted = $false
$auditSsl = @()
$originalStartupHooks = $env:DOTNET_STARTUP_HOOKS

function Wait-ForAuditHosts {
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        if ($auditProcesses[-2].HasExited -or $auditProcesses[-1].HasExited) {
            throw 'API hoặc Web đã dừng khi khởi động.'
        }
        try {
            Invoke-WebRequest https://localhost:47101/api/v1/auth/csrf -SkipCertificateCheck -TimeoutSec 2 | Out-Null
            Invoke-WebRequest https://localhost:47201/login -SkipCertificateCheck -TimeoutSec 2 | Out-Null
            return
        } catch { Start-Sleep -Milliseconds 400 }
    }
    throw 'API/Web local chưa sẵn sàng.'
}

function Save-AuditLoadedRuntime([Diagnostics.Process]$Api, [Diagnostics.Process]$Web) {
    $hosts = foreach ($process in @($Api, $Web)) {
        $process.Refresh()
        $engine = $process.Modules | Where-Object ModuleName -EQ 'coreclr.dll' | Select-Object -First 1
        $hosting = $process.Modules | Where-Object ModuleName -EQ 'Microsoft.AspNetCore.Hosting.dll' | Select-Object -First 1
        $httpConnections = $process.Modules | Where-Object ModuleName -EQ 'Microsoft.AspNetCore.Http.Connections.dll' | Select-Object -First 1
        [ordered]@{
            host = if ($process.Id -eq $Api.Id) { 'API' } else { 'Web' }
            processId = $process.Id
            coreClrPath = $engine.FileName
            aspNetCoreHostingPath = $hosting.FileName
            aspNetCoreHttpConnectionsPath = $httpConnections.FileName
        }
    }
    ConvertTo-Json -InputObject @($hosts) -Depth 4 | Set-Content -LiteralPath (Join-Path $auditArtifacts 'final-loaded-runtimes.json')
}

function New-AuditSession([string]$Origin) {
    $csrf = Invoke-WebRequest "$Origin/api/v1/auth/csrf" -SkipCertificateCheck -SessionVariable browser
    $headers = @{ 'X-CSRF-TOKEN' = ($csrf.Content | ConvertFrom-Json).token }
    $body = @{ username = 'phase11_staff'; password = $env:PHASE11_PASSWORD } | ConvertTo-Json
    $login = Invoke-WebRequest "$Origin/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body $body -Headers $headers -WebSession $browser -SkipCertificateCheck
    @{
        Browser = $browser
        Csrf = $headers
        Bearer = @{ Authorization = 'Bearer ' + ($login.Content | ConvertFrom-Json).accessToken }
    }
}

function Test-AuditRestart([string]$Origin, $OriginalHosts) {
    $active = New-AuditSession $Origin
    $revoked = New-AuditSession $Origin
    $logout = Invoke-WebRequest "$Origin/api/v1/auth/logout" -Method Post -Headers $revoked.Csrf -WebSession $revoked.Browser -SkipCertificateCheck
    if ($logout.StatusCode -ne 204) { throw 'Không thu hồi được phiên dùng cho smoke restart.' }

    foreach ($original in $OriginalHosts) {
        Stop-Process -Id $original.Id
        $original.WaitForExit()
    }
    $env:ASPNETCORE_URLS = 'https://localhost:47101'
    $script:auditProcesses += Start-Process dotnet -ArgumentList ('"' + $auditApi + '"') -WorkingDirectory $apiWorkingDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput "$auditArtifacts/api.restart.stdout" -RedirectStandardError "$auditArtifacts/api.restart.stderr"
    $env:ASPNETCORE_URLS = 'https://localhost:47201'
    $script:auditProcesses += Start-Process dotnet -ArgumentList ('"' + $auditWeb + '"') -WorkingDirectory $webWorkingDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput "$auditArtifacts/web.restart.stdout" -RedirectStandardError "$auditArtifacts/web.restart.stderr"
    Write-Output ('PID API/Web sau restart: ' + ($auditProcesses[-2..-1].Id -join ', '))
    Wait-ForAuditHosts

    $healthy = Invoke-WebRequest "$Origin/api/v1/auth/me" -Headers $active.Bearer -SkipCertificateCheck -SkipHttpErrorCheck
    $blocked = Invoke-WebRequest "$Origin/api/v1/auth/me" -Headers $revoked.Bearer -SkipCertificateCheck -SkipHttpErrorCheck
    $rotation = Invoke-WebRequest "$Origin/api/v1/auth/refresh" -Method Post -Headers $active.Csrf -WebSession $active.Browser -SkipCertificateCheck -SkipHttpErrorCheck
    if ($healthy.StatusCode -ne 200 -or $blocked.StatusCode -ne 401 -or $rotation.StatusCode -ne 200) {
        throw 'Phiên hoặc khóa không khớp sau restart.'
    }
    $samples = @(1..30 | ForEach-Object {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-WebRequest "$Origin/api/v1/auth/me" -Headers $active.Bearer -WebSession $active.Browser -SkipCertificateCheck -SkipHttpErrorCheck
        if ($response.StatusCode -ne 200) { throw 'Probe latency trả status khác 200.' }
        $watch.Elapsed.TotalMilliseconds
    } | Sort-Object)
    [ordered]@{
        restartActiveStatus = $healthy.StatusCode
        restartRevokedStatus = $blocked.StatusCode
        restartRefreshStatus = $rotation.StatusCode
        httpSamples = $samples.Count
        p50Ms = [Math]::Round($samples[14], 2)
        p95Ms = [Math]::Round($samples[28], 2)
        maxMs = [Math]::Round($samples[-1], 2)
        runtime = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        logicalProcessors = [Environment]::ProcessorCount
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $auditArtifacts $(if ($Scope -eq 'Proxy') {
        'hardening-12-proxy-restart.json'
    } else { 'release-restart-smoke.json' }))
}

try {
    if ($Scope -eq 'Proxy' -and !$UseRelease) { throw 'Proxy rehearsal cần -UseRelease.' }
    if (($ChromeSession -or $ChromeNotifications) -and $Scope -ne 'Proxy') { throw 'Chrome rehearsal cần Scope Proxy.' }
    if (netstat -ano -p TCP | Select-String ':(47101|47201|47301)\s.*LISTENING') {
        throw 'Có cổng kiểm thử đang được sử dụng.'
    }
    $fixtureConnection = Get-PropFlowFixtureConnection $auditDatabase
    node scripts/authz/inventory.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Inventory không khớp ma trận kỳ vọng.' }
    $env:PROPFLOW_AUTHZ_SCOPE = $Scope
    if ($Scope -eq 'Proxy') {
        $env:PROPFLOW_AUTHZ_LOAD_USERS = [string]$LoadUsers
        [Environment]::SetEnvironmentVariable('Authentication__RateLimits__auth-login__PermitLimit', '500')
        [Environment]::SetEnvironmentVariable('Authentication__RateLimits__auth-session__PermitLimit', '500')
        $env:ReverseProxy__KnownProxies__0 = '127.0.0.1'

        $env:PROPFLOW_AUTHZ_CERT = Join-Path $auditArtifacts "$auditDatabase.pem"
        $env:PROPFLOW_AUTHZ_PFX = Join-Path $auditArtifacts "$auditDatabase.pfx"
        $env:PROPFLOW_AUTHZ_PFX_PASSWORD = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
        $auditSsl = @($env:PROPFLOW_AUTHZ_CERT, $env:PROPFLOW_AUTHZ_PFX)
        $tlsKey = [Security.Cryptography.RSA]::Create(2048)
        try {
            $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
                'CN=localhost', $tlsKey, [Security.Cryptography.HashAlgorithmName]::SHA256,
                [Security.Cryptography.RSASignaturePadding]::Pkcs1)
            $names = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
            $names.AddDnsName('localhost')
            $names.AddIpAddress([Net.IPAddress]::Loopback)
            $request.CertificateExtensions.Add($names.Build())
            $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddHours(1))
            try {
                [IO.File]::WriteAllText($env:PROPFLOW_AUTHZ_CERT, $certificate.ExportCertificatePem())
                [IO.File]::WriteAllBytes($env:PROPFLOW_AUTHZ_PFX, $certificate.Export('Pfx', $env:PROPFLOW_AUTHZ_PFX_PASSWORD))
            } finally { $certificate.Dispose() }
        } finally { $tlsKey.Dispose() }
        $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $env:PROPFLOW_AUTHZ_PFX
        $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $env:PROPFLOW_AUTHZ_PFX_PASSWORD
        $env:NODE_EXTRA_CA_CERTS = $env:PROPFLOW_AUTHZ_CERT
    }
    $env:PHASE11_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $env:PROPFLOW_FIXTURE_OWNER = [guid]::NewGuid().ToString('N')
    $auditDatabaseStarted = $true
    dotnet $auditFixture create $auditDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Không tạo được dữ liệu giả.' }
    $env:PROPFLOW_MIGRATION_DATABASE = $fixtureConnection
    dotnet $auditFixture verify $auditDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Database của API browser chưa được xác minh.' }
    $env:ConnectionStrings__PropFlowDatabase = $fixtureConnection
    $auditRsa = [Security.Cryptography.RSA]::Create(2048)
    $env:Authentication__PrivateKeyPem = $auditRsa.ExportRSAPrivateKeyPem()
    $auditRsa.Dispose()
    $env:Authentication__OtpHashKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:Authentication__Issuer = 'propflow-authz-local'
    $env:Authentication__Audience = 'propflow-authz-local'
    $env:Authentication__Mail__Host = '127.0.0.1'
    $env:Authentication__Mail__Port = '1'
    $env:Authentication__Mail__From = 'fixture@example.invalid'
    $env:Cors__AllowedOrigins__0 = 'https://localhost:47201'
    $env:HTTPS_PROXY = 'http://127.0.0.1:1'
    $env:HTTP_PROXY = 'http://127.0.0.1:1'
    $env:NO_PROXY = 'localhost,127.0.0.1'
    $env:Cloudinary__CloudName = 'fixture-disabled'
    $env:Cloudinary__ApiKey = 'fixture-disabled'
    $env:Cloudinary__ApiSecret = 'fixture-disabled'
    $env:Payments__SePay__Enabled = 'false'
    $env:Payments__SePay__OAuth__Enabled = 'false'
    $env:AiChatbot__Gemini__Enabled = 'false'
    $env:AiClassification__Gemini__Enabled = 'false'
    $env:AiRecommendation__Gemini__Enabled = 'false'
    if ($ChromeNotifications -and $env:PROPFLOW_NOTIFICATION_GROUP -eq 'AiAuthorization') {
        $env:DOTNET_STARTUP_HOOKS = (Resolve-Path -LiteralPath $auditFixture).Path
    }
    $env:Logging__LogLevel__Default = 'Error'
    $env:DOTNET_ENVIRONMENT = if ($UseRelease) { 'Production' } else { 'Testing' }
    $env:ASPNETCORE_ENVIRONMENT = $env:DOTNET_ENVIRONMENT
    $env:PROPFLOW_AUTHZ_RELEASE = if ($UseRelease) { '1' } else { '0' }
    $env:DataProtection__KeyPath = Join-Path $auditArtifacts "keys-$auditDatabase"
    $env:ASPNETCORE_URLS = 'https://localhost:47101'
    $auditApi = if ($UseRelease) { Join-Path $auditArtifacts 'build/publish/PropFlow.Api/release/PropFlow.Api.dll' }
        else { Join-Path $auditArtifacts 'build/bin/PropFlow.Api/debug/PropFlow.Api.dll' }
    $apiWorkingDirectory = if ($UseRelease) { Split-Path $auditApi } else { Join-Path $auditRoot 'src/PropFlow.Api' }
    $auditProcesses += Start-Process dotnet -ArgumentList ('"' + $auditApi + '"') -WorkingDirectory $apiWorkingDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput "$auditArtifacts/api.stdout" -RedirectStandardError "$auditArtifacts/api.stderr"
    if (!$UseRelease) {
        $env:DOTNET_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
    }
    $env:ASPNETCORE_URLS = 'https://localhost:47201'
    $auditWeb = if ($UseRelease) { Join-Path $auditArtifacts 'web-build/publish/PropFlow.Web/release/PropFlow.Web.dll' }
        else { Join-Path $auditArtifacts 'web-build/bin/PropFlow.Web/debug/PropFlow.Web.dll' }
    $webWorkingDirectory = if ($UseRelease) { Split-Path $auditWeb } else { Join-Path $auditRoot 'src/PropFlow.Web' }
    $auditProcesses += Start-Process dotnet -ArgumentList ('"' + $auditWeb + '"') -WorkingDirectory $webWorkingDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput "$auditArtifacts/web.stdout" -RedirectStandardError "$auditArtifacts/web.stderr"
    Write-Output ('PID API/Web: ' + ($auditProcesses.Id -join ', '))
    Wait-ForAuditHosts
    if ($UseRelease) {
        $productionSwagger = Invoke-WebRequest https://localhost:47101/swagger/v1/swagger.json -SkipCertificateCheck -SkipHttpErrorCheck -TimeoutSec 2
        if ($productionSwagger.StatusCode -ne 404) { throw 'Swagger phải tắt trong Production.' }
        if ($Scope -ne 'Proxy') { Test-AuditRestart 'https://localhost:47101' $auditProcesses }
    }
    if ($Scope -eq 'Proxy') {
        $originalHosts = $auditProcesses
        $proxy = Start-Process node -ArgumentList 'scripts/authz/loopback-proxy.mjs' -WorkingDirectory $auditRoot -WindowStyle Hidden -PassThru
        $auditProcesses += $proxy
        Write-Output "PID proxy $($proxy.Id)"
        for ($attempt = 0; $attempt -lt 20; $attempt++) {
            if ($proxy.HasExited) { throw 'Proxy dừng khi khởi động.' }
            try {
                Invoke-WebRequest https://127.0.0.1:47301/login -SkipCertificateCheck -TimeoutSec 2 | Out-Null
                break
            } catch { Start-Sleep -Milliseconds 250 }
        }
        Test-AuditRestart 'https://127.0.0.1:47301' $originalHosts
        $worker = $auditProcesses[-2]
        $worker.Refresh()
        $workingSetBefore = $worker.WorkingSet64
        $cpuBefore = $worker.TotalProcessorTime.TotalMilliseconds
        node scripts/authz/proxy-load.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Diễn tập proxy hoặc tải không đạt.' }
        $loadArtifact = Join-Path $auditArtifacts "hardening-12-proxy-load-$LoadUsers.json"
        $loadEvidence = Get-Content -Raw -LiteralPath $loadArtifact | ConvertFrom-Json -AsHashtable
        Start-Sleep -Seconds 2
        $worker.Refresh()
        $loadEvidence['apiProcess'] = [ordered]@{
            workingSetBeforeBytes = $workingSetBefore
            workingSetTwoSecondsAfterBytes = $worker.WorkingSet64
            cpuMillisecondsDuringProbe = [Math]::Round($worker.TotalProcessorTime.TotalMilliseconds - $cpuBefore, 2)
        }
        $loadEvidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $loadArtifact
        if ($ChromeSession -or $ChromeNotifications) {
            Save-AuditLoadedRuntime $auditProcesses[-2] $auditProcesses[-1]
        }
        if ($ChromeSession) {
            $env:PROPFLOW_AUTHZ_SCOPE = 'Session'
            $env:PROPFLOW_AUTHZ_SAME_ORIGIN = '1'
            node scripts/authz/browser.mjs
            if ($LASTEXITCODE -ne 0) { throw 'Phiên Chrome qua proxy không đạt.' }
        }
        if ($ChromeNotifications) {
            node scripts/authz/notification-browser.mjs
            if ($LASTEXITCODE -ne 0) { throw 'Chrome notification qua proxy không đạt.' }
        }
    } else {
        node scripts/authz/browser.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Chrome matrix exit code khác 0.' }
    }
} finally {
    $env:DOTNET_STARTUP_HOOKS = $originalStartupHooks
    foreach ($auditProcess in $auditProcesses) {
        if (!$auditProcess.HasExited) { Stop-Process -Id $auditProcess.Id -ErrorAction Stop }
        $auditProcess.WaitForExit()
    }
    if ($auditProcesses.Count) { Write-Output ('Đã dừng PID rehearsal: ' + ($auditProcesses.Id -join ', ')) }
    $auditImageDir = Join-Path $auditRoot ('src/PropFlow.Api/App_Data/service-request-evidence/phase11-' + $auditDatabase)
    if (Test-Path -LiteralPath $auditImageDir) {
        Remove-Item -LiteralPath (Join-Path $auditImageDir 'synthetic.png')
        Remove-Item -LiteralPath $auditImageDir
    }
    if ($auditDatabaseStarted) { dotnet $auditFixture drop $auditDatabase }
    foreach ($file in $auditSsl) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    $keyDirectory = Join-Path $auditArtifacts "keys-$auditDatabase"
    if (Test-Path -LiteralPath $keyDirectory) {
        Get-ChildItem -LiteralPath $keyDirectory -Filter 'key-*.xml' -File | ForEach-Object {
            Remove-Item -LiteralPath $_.FullName
        }
        if (!(Get-ChildItem -LiteralPath $keyDirectory -Force)) {
            Remove-Item -LiteralPath $keyDirectory
        }
    }
    if (netstat -ano -p TCP | Select-String ':(47101|47201|47301)\s.*LISTENING') { Write-Output 'Cổng hiện có listener, cần đối chiếu PID.' }
    else { Write-Output 'Không còn listener trên các cổng test.' }
}
