param(
    [Parameter(Mandatory)][string[]]$Trx,
    [string]$ApiProbes
)
$ErrorActionPreference = 'Stop'

$rules = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'expected-authorization.json') | ConvertFrom-Json
$cases = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'mutation-cases.json') | ConvertFrom-Json
$overrides = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'operation-behavior.json') | ConvertFrom-Json -AsHashtable
$results = @(foreach ($path in $Trx) {
    [xml]$run = Get-Content -Raw -LiteralPath $path
    $run.TestRun.Results.UnitTestResult
})
$probeCounts = @{}
if ($ApiProbes) {
    $probes = Get-Content -Raw -LiteralPath $ApiProbes | ConvertFrom-Json
    foreach ($probe in $probes | Where-Object verdict -EQ 'PASS') {
        $operation = "$($probe.method) $($probe.path)"
        $probeCounts[$operation]++
    }
}
$mutationMethods = @('POST', 'PUT', 'PATCH', 'DELETE')
$verbs = @($rules.endpoints | Where-Object { $_.method -in $mutationMethods })
$inventory = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../../artifacts/authz/endpoints.json') | ConvertFrom-Json
$observed = @{}
foreach ($item in $inventory) { $observed["$($item.method) $($item.path)"] = $item }
$expected = @{}
foreach ($item in $rules.endpoints) { $expected["$($item.method) $($item.path)"] = $item }
if ($observed.Count -ne $expected.Count -or @($expected.Keys | Where-Object { !$observed.ContainsKey($_) }).Count) {
    throw 'Inventory cần được tạo lại bằng inventory.mjs.'
}
foreach ($key in $overrides.Keys) {
    if (!$expected.ContainsKey($key)) { throw "Phân loại hành vi không có endpoint $key" }
}

function Get-Category($entry) {
    switch -Regex ($entry.path) {
        '/webhooks?/|/webhook$' { return 'Webhook' }
        '/evidence$|/avatar$|/attachments$' { return 'File mutation' }
        '/auth/' { return 'Authentication lifecycle' }
        '/sepay/(oauth|accounts|connection)/' { return 'Provider-dependent' }
        '/administration/|/payment-settings/' { return 'Administrative mutation' }
        '/billing/|/payments|/payment/' { return 'Financial mutation' }
        '/classification|/recommendation|/chatbot/ask|/draft-check' { return 'AI trigger' }
        '/announcements|/notifications|/support-chat|/emergency-calls' { return 'Communication mutation' }
        '/assignment|/status|/review|/progress|/close|/start|/cancel|/unpublish' { return 'Workflow transition' }
    }
    switch ($entry.method) {
        POST { 'Create' }
        DELETE { 'Delete' }
        default { 'Update' }
    }
}

$verifiedCases = @{}
foreach ($entry in $cases) {
    $endpoint = "$($entry.method) $($entry.path)"
    if (!$expected.ContainsKey($endpoint) -or $verifiedCases.ContainsKey($endpoint)) { throw "Mục fixture sai hoặc trùng $endpoint" }
    $pattern = '\.' + [regex]::Escape($entry.test)
    if ($entry.case) { $pattern += '\([^)]*"' + [regex]::Escape($entry.case) + '"\)$' }
    elseif ($entry.parameters) { $pattern += '\(' + [regex]::Escape($entry.parameters) + '\)$' }
    else { $pattern += '$' }
    $match = @($results | Where-Object { $_.testName -match $pattern })
    $evidenceNames = @($match | Select-Object -ExpandProperty testName)
    $allCasesPassed = $match.Count -eq 1 -and $match[0].outcome -eq 'Passed'
    foreach ($parameters in $entry.additionalParameters) {
        $extraPattern = '\.' + [regex]::Escape($entry.test) + '\(' + [regex]::Escape($parameters) + '\)$'
        $extra = @($results | Where-Object { $_.testName -match $extraPattern })
        $evidenceNames += @($extra | Select-Object -ExpandProperty testName)
        $allCasesPassed = $allCasesPassed -and $extra.Count -eq 1 -and $extra[0].outcome -eq 'Passed'
    }
    $verifiedCases[$endpoint] = @{
        test = $entry.test
        resourceScope = $entry.resourceScope
        effect = $entry.effect
        gap = $entry.gap
        asserted = @($entry.asserted)
        outcome = if ($allCasesPassed) { 'Passed' } elseif ($match.Count -eq 1 -and $match[0].outcome -ne 'Passed') { $match[0].outcome } else { 'NotVerified' }
        evidence = if ($allCasesPassed) { $evidenceNames -join ' | ' } else { $null }
    }
}

$rows = @(
    foreach ($entry in $rules.endpoints) {
        $operation = "$($entry.method) $($entry.path)"
        $route = $observed[$operation]
        $behavior = $overrides[$operation]
        $applies = if ($behavior) { $behavior.applicable } else { $entry.method -in $mutationMethods }
        $category = if ($behavior) { $behavior.category } elseif ($applies) { Get-Category $entry } else { 'Read' }
        $test = $verifiedCases[$operation]
        if ($test -and !$applies) { throw "Fixture mutation ngoài mẫu số $operation" }
        $verified = $test -and $test.outcome -eq 'Passed' -and !$test.gap
        $status = if (!$applies) { 'NOT APPLICABLE' } elseif ($verified) { 'VERIFIED' }
            elseif ($test -or $probeCounts[$operation]) { 'PARTIALLY VERIFIED' } else { 'NOT VERIFIED' }
        $gap = if ($verified -or !$applies) { $null }
            elseif ($test -and $test.outcome -ne 'Passed') { 'Đã chỉ định test, cần TRX PASS duy nhất khớp đúng ca.' }
            elseif ($test) { $test.gap }
            elseif ($category -in @('Provider-dependent', 'Webhook', 'AI trigger')) { 'Cần đối chứng HTTP, fixture kiểm tra tác động và provider stub. Kiểm chứng provider thật ghi riêng.' }
            else { "Cần đối chiếu fixture $($route.action) với payload, đối chứng HTTP và snapshot tác động." }
        [ordered]@{
            operationId = $operation
            feature = $route.feature
            controller = $route.file
            action = $route.action
            observedGuard = $route.guard
            category = $category
            applicable = $applies
            expectedAuthorization = @($entry.allow)
            resourceScope = if ($test.resourceScope) { $test.resourceScope } elseif ($entry.path.Contains('{')) { 'Định danh trên route, cần đối chứng phạm vi phù hợp' } else { 'Phạm vi theo hành vi của endpoint' }
            effect = $test.effect ?? $behavior.effect
            validFixture = [bool]$test -and (!$test.gap -or 'validFixture' -in $test.asserted)
            positiveControl = [bool]$test -and (!$test.gap -or 'positiveControl' -in $test.asserted)
            negativeControl = [bool]$test -and (!$test.gap -or 'negativeControl' -in $test.asserted)
            sideEffectAssertions = [bool]$test -and (!$test.gap -or 'sideEffectAssertions' -in $test.asserted)
            deniedProbesPassed = [int]$probeCounts[$operation]
            liveProvider = if ($category -in @('Provider-dependent', 'Webhook', 'AI trigger')) { 'NOT VERIFIED' } else { $null }
            fixtureTest = $test.test
            testOutcome = $test.outcome
            evidence = $test.evidence
            status = $status
            gap = $gap
        }
    }
)
$applicable = @($rows | Where-Object applicable)
$passed = @($applicable | Where-Object status -EQ 'VERIFIED').Count
[ordered]@{
    httpOperations = $rows.Count
    conventionalWriteVerbs = $verbs.Count
    applicableOperations = $applicable.Count
    verifiedFixtureEndpoints = $passed
    coveragePercent = [Math]::Round(100 * $passed / $applicable.Count, 2)
    statusCounts = @($rows | Group-Object { $_['status'] } | ForEach-Object { @{ status = $_.Name; count = $_.Count } })
    rows = $rows
} | ConvertTo-Json -Depth 6
