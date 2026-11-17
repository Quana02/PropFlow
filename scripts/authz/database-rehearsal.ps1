param([string]$PgBin)
$ErrorActionPreference = 'Stop'
$artifacts = (Resolve-Path 'artifacts/authz').Path
$fixture = Join-Path $artifacts 'fixture-build/bin/BrowserFixture/debug/BrowserFixture.dll'
. (Join-Path $PSScriptRoot 'test-database.ps1')
$conn = [Data.Common.DbConnectionStringBuilder]::new()
$conn.set_ConnectionString($env:PROPFLOW_TEST_DATABASE)
$server = [string]$conn['Host']
if ($server -notin @('localhost','127.0.0.1','::1')) { throw 'Cần PostgreSQL fixture trên loopback.' }
if (!$conn.ContainsKey('Username')) { throw 'Cần Username cho PostgreSQL fixture.' }

function Find-PgTool([string]$Name) {
    if ($PgBin) { return (Get-Command (Join-Path $PgBin "$Name.exe") -CommandType Application).Source }
    (Get-Command $Name -CommandType Application).Source
}
$dumpTool = Find-PgTool 'pg_dump'
$restoreTool = Find-PgTool 'pg_restore'
$psqlTool = Find-PgTool 'psql'
$database = 'propflow_phase11_' + [guid]::NewGuid().ToString('N')
$clone = 'propflow_phase11_' + [guid]::NewGuid().ToString('N')
$archive = "artifacts/authz/$database.dump"
$created = [Collections.Generic.List[string]]::new()
$previous = @{ PGHOST = $env:PGHOST; PGPORT = $env:PGPORT; PGUSER = $env:PGUSER; PGPASSWORD = $env:PGPASSWORD }

function Read-FixtureSnapshot([string]$Name) {
    $sql = @'
SELECT jsonb_build_object(
  'accounts', (SELECT count(*) FROM auth.users),
  'roleAssignments', (SELECT count(*) FROM administration.user_role_assignments),
  'residents', (SELECT count(*) FROM residents.residents),
  'invoices', (SELECT count(*) FROM billing.invoices),
  'assignments', (SELECT count(*) FROM maintenance.maintenance_assignments),
  'results', (SELECT count(*) FROM maintenance.maintenance_results),
  'authMigrations', (SELECT jsonb_agg("MigrationId" ORDER BY "MigrationId") FROM auth."__EFMigrationsHistory")
);
'@
    $json = & $psqlTool --no-password --no-psqlrc --tuples-only --no-align --set=ON_ERROR_STOP=1 --dbname $Name --command $sql
    if ($LASTEXITCODE -ne 0) { throw 'Không đọc được snapshot PostgreSQL.' }
    $json
}

try {
    $env:PGHOST = $server
    $env:PGPORT = if ($conn.ContainsKey('Port')) { $conn['Port'] } else { '5432' }
    $env:PGUSER = $conn['Username']
    $env:PGPASSWORD = $conn['Password']
    $env:PHASE11_PASSWORD = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $env:PROPFLOW_FIXTURE_OWNER = [guid]::NewGuid().ToString('N')

    $created.Add($database)
    dotnet $fixture create $database
    if ($LASTEXITCODE) { throw 'Tạo nguồn backup thất bại.' }
    $before = Read-FixtureSnapshot $database
    & $dumpTool --no-password --format=custom --no-owner --no-acl --file $archive --dbname $database
    if ($LASTEXITCODE) { throw 'pg_dump thất bại.' }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash

    $created.Add($clone)
    dotnet $fixture create-empty $clone
    if ($LASTEXITCODE) { throw 'Tạo đích restore thất bại.' }
    $env:PROPFLOW_MIGRATION_DATABASE = Get-PropFlowFixtureConnection $clone
    dotnet $fixture verify $clone
    if ($LASTEXITCODE) { throw 'Đích restore chưa được xác minh.' }
    & $restoreTool --no-password --exit-on-error --single-transaction --no-owner --no-acl --dbname $clone $archive
    if ($LASTEXITCODE) { throw 'pg_restore thất bại.' }
    $after = Read-FixtureSnapshot $clone
    if ($before -cne $after) { throw 'Snapshot fixture khác sau restore.' }
    $status = @(dotnet $fixture status $clone)
    if ($LASTEXITCODE -or @($status | Where-Object { $_ -notmatch 'pending=0$' }).Count -or $status.Count -ne 14) {
        throw 'Schema clone còn migration hoặc inventory DbContext không khớp.'
    }
    [ordered]@{
        archiveSha256 = $hash
        archiveBytes = (Get-Item -LiteralPath $archive).Length
        restored = $true
        checkedContexts = $status.Count
        snapshot = $after | ConvertFrom-Json
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifacts 'hardening-12-database-recovery.json')
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
    [Environment]::SetEnvironmentVariable('PROPFLOW_MIGRATION_DATABASE', $null)
    foreach ($name in $created) {
        dotnet $fixture drop $name
        if ($LASTEXITCODE) { Write-Warning "Cần đối chiếu fixture local $name" }
    }
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
    $imageDirectory = Join-Path (Get-Location).Path "src/PropFlow.Api/App_Data/service-request-evidence/phase11-$database"
    if (Test-Path -LiteralPath $imageDirectory) {
        Remove-Item -LiteralPath (Join-Path $imageDirectory 'synthetic.png')
        Remove-Item -LiteralPath $imageDirectory
    }
}
