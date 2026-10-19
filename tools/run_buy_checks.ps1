param([Parameter(Mandatory = $true)][string]$Godot, [ValidateRange(1,65535)][int]$Port = 27216)
$ErrorActionPreference = 'Stop'
$buyProject = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$buyExecutable = (Resolve-Path -LiteralPath $Godot).Path
$buyRun = [Guid]::NewGuid().ToString('N')
$buyLogs = Join-Path ([IO.Path]::GetTempPath()) "contrapoint-buy-$buyRun"
[IO.Directory]::CreateDirectory($buyLogs) | Out-Null
$buyProcesses = @{}
try {
    foreach ($buyRole in @('host', 'client', 'late')) {
        $buyArgs = @('--headless', '--audio-driver', 'Dummy', '--max-fps', '120', '--path', "`"$buyProject`"", 'res://scenes/debug/buy_lan_checks.tscn', '--', '--console-test-profile', '--menu-test-profile', "--buy-role=$buyRole", "--buy-run=$buyRun", "--lan-port=$Port")
        $buyProcesses[$buyRole] = Start-Process -FilePath $buyExecutable -ArgumentList $buyArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $buyLogs "$buyRole.log") -RedirectStandardError (Join-Path $buyLogs "$buyRole.err")
        $buyProcesses[$buyRole].Handle | Out-Null
    }
    $buyDeadline = [DateTime]::UtcNow.AddSeconds(150)
    while (@($buyProcesses.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $buyDeadline) { throw "Buy checks timed out. Logs: $buyLogs" }
        Start-Sleep -Milliseconds 250
    }
    foreach ($buyRole in @('host', 'client', 'late')) {
        $buyProcesses[$buyRole].WaitForExit()
        $buyOutput = Get-Content -LiteralPath (Join-Path $buyLogs "$buyRole.log") -Raw
        $buyErrors = Get-Content -LiteralPath (Join-Path $buyLogs "$buyRole.err") -Raw
        Write-Output ($buyOutput -split '\r?\n' | Where-Object { $_ -match '^BUY_LAN_CHECKS ' })
        if ($buyProcesses[$buyRole].ExitCode -ne 0 -or $buyOutput -notmatch "BUY_LAN_CHECKS role=$buyRole checks=\d+ failures=0" -or $buyErrors -match 'ERROR:|SCRIPT ERROR:') { throw "Buy checks failed for $buyRole. Logs: $buyLogs" }
    }
    Write-Output "Logs: $buyLogs"
} finally {
    foreach ($buyProcess in $buyProcesses.Values) { if (-not $buyProcess.HasExited) { $buyProcess.Kill() }; $buyProcess.Dispose() }
}
