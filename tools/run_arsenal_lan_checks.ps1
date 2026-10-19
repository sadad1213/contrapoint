param(
    [Parameter(Mandatory=$true)][string]$Godot,
    [string[]]$Weapons = @('mp5navy', 'm3', 'famas'),
    [int]$Port = 27027
)
$ErrorActionPreference = 'Stop'
$arsenalProject = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$arsenalExecutable = (Resolve-Path -LiteralPath $Godot).Path
foreach ($arsenalWeapon in $Weapons) {
    if ($arsenalWeapon -notmatch '^[a-z0-9]+$') { throw 'Invalid weapon name' }
    $arsenalRun = [Guid]::NewGuid().ToString('N')
    $arsenalLogs = Join-Path ([IO.Path]::GetTempPath()) "contrapoint-arsenal-lan-$arsenalWeapon-$arsenalRun"
    [IO.Directory]::CreateDirectory($arsenalLogs) | Out-Null
    $arsenalProcesses = @{}
    try {
        foreach ($arsenalRole in @('host', 'client')) {
            $arsenalArgs = @('--headless', '--audio-driver', 'Dummy', '--max-fps', '120', '--path', "`"$arsenalProject`"",
                'res://scenes/debug/weapon_pickup_lan_checks.tscn', '--', '--console-test-profile', '--menu-test-profile',
                "--weapon-run=$arsenalRun", "--weapon-kind=$arsenalWeapon", "--lan-port=$Port")
            if ($arsenalRole -eq 'host') { $arsenalArgs += '--weapon-host' }
            $arsenalProcesses[$arsenalRole] = Start-Process -FilePath $arsenalExecutable -ArgumentList $arsenalArgs -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $arsenalLogs "$arsenalRole.log") -RedirectStandardError (Join-Path $arsenalLogs "$arsenalRole.err")
            $arsenalProcesses[$arsenalRole].Handle | Out-Null
        }
        $arsenalDeadline = [DateTime]::UtcNow.AddSeconds(150)
        while (@($arsenalProcesses.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
            if (@($arsenalProcesses.Values | Where-Object { $_.HasExited -and $_.ExitCode -ne 0 }).Count -gt 0) {
                throw "Arsenal LAN process failed: $arsenalLogs"
            }
            if ([DateTime]::UtcNow -gt $arsenalDeadline) { throw "Arsenal LAN timeout: $arsenalLogs" }
            Start-Sleep -Milliseconds 250
        }
        foreach ($arsenalRole in @('host', 'client')) {
            $arsenalProcesses[$arsenalRole].WaitForExit()
            $arsenalOutput = Get-Content -LiteralPath (Join-Path $arsenalLogs "$arsenalRole.log") -Raw
            $arsenalErrors = Get-Content -LiteralPath (Join-Path $arsenalLogs "$arsenalRole.err") -Raw
            $arsenalOutput -split '\r?\n' | Where-Object { $_ -match '^WEAPON_PICKUP_LAN_CHECKS ' } | Write-Output
            if ($arsenalProcesses[$arsenalRole].ExitCode -ne 0 -or $arsenalOutput -notmatch 'WEAPON_PICKUP_LAN_CHECKS .*failures=0' -or $arsenalErrors -match 'SCRIPT ERROR:') {
                throw "Arsenal LAN failed ($arsenalWeapon/$arsenalRole): $arsenalLogs"
            }
        }
        Write-Output "Logs: $arsenalLogs"
    } finally {
        foreach ($arsenalProcess in $arsenalProcesses.Values) {
            if (-not $arsenalProcess.HasExited) { $arsenalProcess.Kill() }
            $arsenalProcess.Dispose()
        }
    }
}
