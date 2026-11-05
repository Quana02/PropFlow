param()
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskPreviousConnection = $env:PROPFLOW_TEST_DATABASE
try {
    if (-not $env:PROPFLOW_TEST_DATABASE) {
        $taskSecretsPath = Join-Path $env:APPDATA 'Microsoft/UserSecrets/adbac45b-651b-498f-a797-12585846625f/secrets.json'
        if (-not (Test-Path -LiteralPath $taskSecretsPath)) { throw 'Configure PROPFLOW_TEST_DATABASE with a local PostgreSQL admin connection.' }
        $taskSecrets = Get-Content -LiteralPath $taskSecretsPath -Raw | ConvertFrom-Json
        $env:PROPFLOW_TEST_DATABASE = $taskSecrets.'ConnectionStrings:PropFlowDatabase'
    }
    Push-Location $taskRepo
    try {
        dotnet test tests/PropFlow.BillingCycleTests/PropFlow.BillingCycleTests.csproj --logger 'console;verbosity=normal'
        $taskExitCode = $LASTEXITCODE
    } finally { Pop-Location }
} finally {
    if ($taskPreviousConnection) { $env:PROPFLOW_TEST_DATABASE = $taskPreviousConnection }
    else { Remove-Item Env:PROPFLOW_TEST_DATABASE -ErrorAction SilentlyContinue }
}
exit $taskExitCode
