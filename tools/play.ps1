<#
.SYNOPSIS
  Play Keyrun: one line from the repo root (D65/D66, HUMAN-PLAY).

.DESCRIPTION
  Builds the game (dotnet build game/Keyrun.csproj -c Debug, incremental: seconds when nothing changed), imports the
  CC0 models once if this checkout has never imported them (tools/godot/import.ps1), then starts the game in a
  1280x720 window through tools/godot/godot.ps1 (the launch lock and DOTNET_ROOT) with no --autorun:

    -- --camera vertical --log %LOCALAPPDATA%/keyrun/play-<yyyyMMdd-HHmmss>.log

  The game opens on the pre-run screen (Character tab). Pick Warden, Chanter or Cinder there, press Enter to queue,
  Enter again at "Group found", and play the tutorial dungeon (Hollow Vault, key 2) as seat 1 while bots take the two
  other roles. game/PLAY.md lists the keys.

  -Dungeon picks the dungeon by id and passes --dungeon <id> to the game (D65): hollow-vault (the default, the
  tutorial) or sunken-bellfoundry (three bosses). An id the content lacks stops the launch, naming the known ids.

  The play session is capped at -Hours (default 4) through KEYRUN_GODOT_TIMEOUT_SEC (the wrapper has no uncapped
  launch). -Record also writes the session's keys to play-<stamp>.input-log.json beside the log (--record-input), so a
  session can be replayed with --replay-input.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/play.ps1

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/play.ps1 -Dungeon sunken-bellfoundry
#>
param(
    [switch]$Record,
    [string]$Dungeon = "hollow-vault",
    [ValidateRange(1, 24)][int]$Hours = 4
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'godot/preflight.ps1')

# Same .NET resolution as tools/godot/godot.ps1: DOTNET_ROOT, then %USERPROFILE%\.dotnet, then PATH.
$dotnet = 'dotnet'
$dotnetRoot = $env:DOTNET_ROOT
if (-not $dotnetRoot -and $env:USERPROFILE) {
    $candidate = Join-Path $env:USERPROFILE '.dotnet'
    if (Test-Path -LiteralPath (Join-Path $candidate 'dotnet.exe') -PathType Leaf) { $dotnetRoot = $candidate }
}
if ($dotnetRoot -and (Test-Path -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe') -PathType Leaf)) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = $dotnetRoot + [IO.Path]::PathSeparator + $env:PATH
    $dotnet = Join-Path $dotnetRoot 'dotnet.exe'
}
if (-not $env:MSBUILDDISABLENODEREUSE) { $env:MSBUILDDISABLENODEREUSE = '1' }

Write-Output 'play: building game/Keyrun.csproj (Debug)...'
& $dotnet build (Join-Path $repo 'game/Keyrun.csproj') -c Debug -nologo -v q
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("play: the build failed (exit $LASTEXITCODE); nothing was launched")
    exit $LASTEXITCODE
}

$import = Invoke-KeyrunImportIfMissing -RepoRoot $repo -ProjectRel 'game'
foreach ($line in $import.Lines) { Write-Output $line }
if (-not $import.Ok) {
    [Console]::Error.WriteLine('play: the model import failed; nothing was launched')
    exit $import.Code
}

$base = $env:LOCALAPPDATA
if (-not $base) { $base = [IO.Path]::GetTempPath() }
$dir = Join-Path $base 'keyrun'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$log = Join-Path $dir "play-$stamp.log"
$userArgs = @('--camera', 'vertical', '--dungeon', $Dungeon, '--log', $log)
if ($Record) { $userArgs += @('--record-input', (Join-Path $dir "play-$stamp.input-log.json")) }

if (-not $env:KEYRUN_GODOT_TIMEOUT_SEC) { $env:KEYRUN_GODOT_TIMEOUT_SEC = [string]($Hours * 3600) }
Write-Output "play: log $log"
Write-Output 'play: Character tab: Up/Down or click a class; Enter queues, Enter again is ready. Keys: game/PLAY.md.'
& (Join-Path $PSScriptRoot 'godot/godot.ps1') --path game --windowed --resolution 1280x720 '--' @userArgs
exit $LASTEXITCODE
