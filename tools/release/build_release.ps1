<#
.SYNOPSIS
    Builds a Contrapoint release, assembles the distributable layout and
    publishes it to the LAN share with a hash manifest.

.DESCRIPTION
    The share layout is the contract between the host machine and the
    launcher that players run:

        <ShareRoot>\current.json                  -> points at the live build
        <ShareRoot>\content\contrapoint\<id>\     -> the game files
        <ShareRoot>\content\contrapoint\<id>\manifest.json

    The manifest lists every file with its size and SHA-256, so a client can
    download only the files that differ from what it already has.

    The executable is exported with a separate PCK (export_presets.cfg sets
    binary_format/embed_pck=false) so the engine template stays byte
    identical between builds and only the content changes.

.EXAMPLE
    .\tools\release\build_release.ps1
    .\tools\release\build_release.ps1 -Godot "C:\Godot\Godot_v4.8-beta1.exe" -ShareRoot "D:\CPShare"
#>
param(
    [string]$Godot = "C:\Users\Hueglot\Downloads\Godot_v4.8-beta1_mono_win64\Godot_v4.8-beta1_mono_win64.exe",
    [string]$Preset = "Windows Desktop",
    [string]$ShareRoot = "C:\CPShare",
    [string]$OutDir = "",
    [int]$KeepBuilds = 3,
    [switch]$SkipExport
)

$ErrorActionPreference = 'Stop'
# Вывод в UTF-8: иначе кириллица в логе ломается при перехвате stdout.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$OutputEncoding = [System.Text.Encoding]::UTF8
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = (Resolve-Path -LiteralPath (Join-Path $repo '..\pc')).Path + '\release' }
$statePath = Join-Path $PSScriptRoot 'release.json'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-JsonFile([string]$Path, $Value) {
    $json = $Value | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

function Get-RelativePath([string]$Base, [string]$Path) {
    $base = $Base.TrimEnd('\')
    $full = $Path
    if (-not $full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$Path' is outside of '$base'"
    }
    return $full.Substring($base.Length).TrimStart('\').Replace('\', '/')
}

# --- build identity -------------------------------------------------------
$today = Get-Date
$dateKey = $today.ToString('yyyyMMdd')
$dayStamp = $today.ToString('ddMMyy')
$state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
$buildsToday = @($state.builds | Where-Object { $_.buildId -like "$dateKey*" }).Count
$buildId = [int]("$dateKey{0:D2}" -f ($buildsToday + 1))
if ($null -ne $state -and $state.lastBuildId -ge $buildId) { $buildId = [int]$state.lastBuildId + 1 }
$version = "v{0}v{1}" -f $dayStamp, ($buildsToday + 1)

Write-Host "Contrapoint release $version (build $buildId)"

# --- stamp the in-game version -------------------------------------------
$projectFile = Join-Path $repo 'project.godot'
$projectText = [System.IO.File]::ReadAllText($projectFile)
$stamped = [regex]::Replace($projectText, 'config/version="[^"]*"', "config/version=`"$version`"")
if ($stamped -ne $projectText) {
    [System.IO.File]::WriteAllText($projectFile, $stamped, $utf8NoBom)
    Write-Host "  project.godot version -> $version"
}

# --- export ---------------------------------------------------------------
if (-not $SkipExport) {
    if (-not (Test-Path -LiteralPath $Godot)) { throw "Godot executable not found: $Godot" }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Get-ChildItem -LiteralPath $OutDir -Filter 'Contrapoint.*' -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem -LiteralPath $OutDir -Filter 'data_*' -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Write-Host "  exporting with $Godot ..."
    # Godot — приложение с графическим интерфейсом: PowerShell не заполняет
    # $LASTEXITCODE после его запуска, поэтому код возврата берём из процесса.
    $exportArgs = @(
        '--headless',
        '--path', ('"' + $repo + '"'),
        '--export-release', ('"' + $Preset + '"'),
        ('"' + (Join-Path $OutDir 'Contrapoint.exe') + '"')
    )
    $exportProcess = Start-Process -FilePath $Godot -ArgumentList $exportArgs -NoNewWindow -Wait -PassThru
    if ($exportProcess.ExitCode -ne 0) { throw "Godot export failed with exit code $($exportProcess.ExitCode)" }
}

$exe = Join-Path $OutDir 'Contrapoint.exe'
$pck = Join-Path $OutDir 'Contrapoint.pck'
if (-not (Test-Path -LiteralPath $exe)) { throw "Export did not produce $exe" }
if (-not (Test-Path -LiteralPath $pck)) { throw "Export did not produce $pck (is binary_format/embed_pck disabled?)" }

# --- manifest -------------------------------------------------------------
$files = @()
$totalSize = [int64]0
Get-ChildItem -LiteralPath $OutDir -Recurse -File | Where-Object { $_.Name -ne 'manifest.json' } | ForEach-Object {
    $relative = Get-RelativePath $OutDir $_.FullName
    $totalSize += [int64]$_.Length
    $files += [pscustomobject][ordered]@{
        path   = $relative
        size   = [int64]$_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$files = @($files | Sort-Object { $_.path })

$protocol = 0
$lanSession = Join-Path $repo 'network\LanSession.cs'
if (Test-Path -LiteralPath $lanSession) {
    $match = [regex]::Match([System.IO.File]::ReadAllText($lanSession), 'Protocol\s*=\s*(\d+)')
    if ($match.Success) { $protocol = [int]$match.Groups[1].Value }
}

$manifest = [ordered]@{
    schema      = 1
    app         = 'contrapoint'
    buildId     = $buildId
    version     = $version
    createdAt   = $today.ToString('o')
    protocol    = $protocol
    minProtocol = $protocol
    exe         = 'Contrapoint.exe'
    totalSize   = $totalSize
    files       = $files
}

# --- publish --------------------------------------------------------------
$appDir = Join-Path $ShareRoot 'content\contrapoint'
$buildDir = Join-Path $appDir $buildId
if (Test-Path -LiteralPath $buildDir) { Remove-Item -LiteralPath $buildDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

foreach ($file in $files) {
    $target = Join-Path $buildDir $file.path.Replace('/', '\')
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $OutDir $file.path.Replace('/', '\')) -Destination $target -Force
}
Write-JsonFile (Join-Path $buildDir 'manifest.json') $manifest
Write-JsonFile (Join-Path $ShareRoot 'current.json') ([ordered]@{
    schema    = 1
    app       = 'contrapoint'
    buildId   = $buildId
    version   = $version
    protocol  = $protocol
    createdAt = $today.ToString('o')
    dir       = "content/contrapoint/$buildId"
    manifest  = "content/contrapoint/$buildId/manifest.json"
})

# --- state ----------------------------------------------------------------
$history = @()
if ($null -ne $state) { $history = @($state.builds) }
$history += [ordered]@{ buildId = $buildId; version = $version; createdAt = $today.ToString('o'); files = $files.Count }
Write-JsonFile $statePath ([ordered]@{ lastBuildId = $buildId; builds = $history })

# --- prune old builds ------------------------------------------------------
if ($KeepBuilds -gt 0) {
    $stale = Get-ChildItem -LiteralPath $appDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne $buildId } |
        Sort-Object { [int64]$_.Name } -Descending |
        Select-Object -Skip ($KeepBuilds - 1)
    foreach ($old in $stale) {
        Write-Host "  removing old build $($old.Name)"
        Remove-Item -LiteralPath $old.FullName -Recurse -Force
    }
}

# --- summary --------------------------------------------------------------
Write-Host ""
Write-Host "  build   : $version (id $buildId)"
Write-Host "  files   : $($files.Count), total $([math]::Round($manifest.totalSize / 1MB, 1)) MB"
foreach ($file in $files) {
    if ($file.size -lt 1MB) { continue }
    Write-Host ("    {0,-52} {1,8:N1} MB  {2}" -f $file.path, ($file.size / 1MB), $file.sha256.Substring(0, 12))
}
$small = @($files | Where-Object { $_.size -lt 1MB }).Count
if ($small -gt 0) { Write-Host "    ещё $small файлов меньше 1 МБ" }
Write-Host "  published: $buildDir"
