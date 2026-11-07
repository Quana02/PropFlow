param([string]$BaseUrl = 'https://localhost:7222')
$ErrorActionPreference = 'Stop'
$html = (Invoke-WebRequest "$BaseUrl/").Content
$links = [regex]::Matches($html, '<link\b[^>]*href="([^"]+)"[^>]*>') |
    ForEach-Object { [System.Net.WebUtility]::HtmlDecode($_.Groups[1].Value) }
if ($links | Where-Object { $_ -match 'PropFlow\.Web\.Client.*bundle\.scp\.css' }) {
    throw 'Client CSS must only load through the host import; a duplicate can override new styles with a cached bundle.'
}
$hostCss = @($links | Where-Object { $_ -match '^PropFlow\.Web\.styles\.css\?v=[A-F0-9]{64}$' })
if ($hostCss.Count -ne 1) { throw 'Expected one host stylesheet with a content hash.' }
$css = (Invoke-WebRequest "$BaseUrl/$($hostCss[0])").Content
$import = [regex]::Match($css, '@import\s+[''"]([^''"]+)[''"]')
if (-not $import.Success) { throw 'Host stylesheet has no component bundle import.' }
$clientCss = (Invoke-WebRequest "$BaseUrl/$($import.Groups[1].Value)").Content
foreach ($marker in @('.pricing-dialog-header', '.pricing-form-actions', 'backdrop-filter:blur(5px)', 'height:44px')) {
    if (-not $clientCss.Contains($marker)) { throw "Live CSS is missing pricing style: $marker" }
}
if ($clientCss -match '\.pricing-dialog\[[^\]]+\]\s+button\s*\{[^}]*background:#0089bc') {
    throw 'The old dialog rule still paints every dropdown/calendar button blue.'
}
Write-Output 'PASS: one versioned host stylesheet, current client import, new pricing styles, no old blanket button rule.'
Write-Output "Stylesheet: $($hostCss[0])"
Write-Output "Import: $($import.Groups[1].Value)"
