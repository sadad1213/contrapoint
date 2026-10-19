param(
    [Parameter(Mandatory = $true)][string]$Godot,
    [ValidatePattern('^[a-z0-9_-]+$')][string]$Map = 'inferno',
    [ValidateRange(1,65535)][int]$Port = 27016,
    [switch]$Deathmatch,
    [switch]$Movement,
    [switch]$World,
    [switch]$Cheats
)
$ErrorActionPreference = 'Stop'
if (@($Deathmatch, $Movement, $World, $Cheats).Where({ $_ }).Count -gt 1) { throw 'Choose one of -Deathmatch, -Movement, -World or -Cheats.' }
$lanProject = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path -LiteralPath (Join-Path $lanProject "scenes/maps/$Map.tscn") -PathType Leaf)) {
    throw "Map scene not found: scenes/maps/$Map.tscn"
}
$lanExecutable = (Resolve-Path -LiteralPath $Godot).Path
$lanRun = [Guid]::NewGuid().ToString('N')
$lanLogs = Join-Path ([IO.Path]::GetTempPath()) "contrapoint-lan-$lanRun"
[IO.Directory]::CreateDirectory($lanLogs) | Out-Null
$lanProcesses = @{}
$lanCheckPrefix = if ($Deathmatch) { 'DM_CHECKS' } else { 'LAN_CHECKS' }
$lanCheckScene = if ($Deathmatch) { 'deathmatch_checks' } else { 'lan_checks' }
$lanArgPrefix = if ($Deathmatch) { 'dm' } else { 'lan' }
$lanRoles = if ($Movement -or $Cheats) { @('host', 'client') } else { @('host', 'client', 'late') }
try {
    foreach ($lanRole in $lanRoles) {
        $lanArgs = @('--headless', '--audio-driver', 'Dummy', '--max-fps', '120', '--path', "`"$lanProject`"", "res://scenes/debug/$lanCheckScene.tscn", '--', '--console-test-profile', '--menu-test-profile', "--$lanArgPrefix-role=$lanRole", "--$lanArgPrefix-run=$lanRun", "--lan-map=$Map", "--lan-port=$Port")
        if ($Movement) { $lanArgs += '--lan-movement-only' }
        if ($World) { $lanArgs += '--lan-world-only' }
        if ($Cheats) { $lanArgs += '--lan-cheats-only' }
        $lanProcesses[$lanRole] = Start-Process -FilePath $lanExecutable -ArgumentList $lanArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $lanLogs "$lanRole.log") -RedirectStandardError (Join-Path $lanLogs "$lanRole.err")
        $lanProcesses[$lanRole].Handle | Out-Null
    }
    $lanDeadline = [DateTime]::UtcNow.AddSeconds(210)
    while (@($lanProcesses.Values | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $lanDeadline) { throw "LAN checks timed out. Logs: $lanLogs" }
        Start-Sleep -Milliseconds 250
    }
    foreach ($lanRole in $lanRoles) {
        $lanProcesses[$lanRole].WaitForExit()
        $lanOutput = Get-Content -LiteralPath (Join-Path $lanLogs "$lanRole.log") -Raw
        $lanErrors = Get-Content -LiteralPath (Join-Path $lanLogs "$lanRole.err") -Raw
        $lanSummary = $lanOutput -split '\r?\n' | Where-Object { $_ -match "^$lanCheckPrefix " }
        Write-Output $lanSummary
        if ($lanProcesses[$lanRole].ExitCode -ne 0 -or $lanOutput -notmatch "$lanCheckPrefix role=$lanRole checks=\d+ failures=0" -or $lanErrors -match 'ERROR:|SCRIPT ERROR:') {
            throw "LAN checks failed for $lanRole (exit $($lanProcesses[$lanRole].ExitCode)). Logs: $lanLogs"
        }
    }
    Write-Output "Logs: $lanLogs"
} finally {
    foreach ($lanProcess in $lanProcesses.Values) {
        if (-not $lanProcess.HasExited) { $lanProcess.Kill() }
        $lanProcess.Dispose()
    }
}
