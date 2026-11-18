function Get-PropFlowFixtureConnection([string]$Name) {
    if (!$env:PROPFLOW_TEST_DATABASE) { throw 'Cần PROPFLOW_TEST_DATABASE trỏ tới database propflow_test.' }
    $connection = [Data.Common.DbConnectionStringBuilder]::new()
    $connection.set_ConnectionString($env:PROPFLOW_TEST_DATABASE)
    if ([string]$connection['Database'] -cne 'propflow_test') { throw 'Nguồn phải là database propflow_test đã được cấp phép.' }
    $connection['Database'] = $Name
    $serialized = $connection.get_ConnectionString()
    $reparsed = [Data.Common.DbConnectionStringBuilder]::new()
    $reparsed.set_ConnectionString($serialized)
    if ([string]$reparsed['Database'] -cne $Name) { throw 'Database fixture thực tế khác tên của lượt chạy.' }
    $serialized
}
