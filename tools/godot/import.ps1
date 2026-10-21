<#
.SYNOPSIS
  Fill the Godot import cache of the Keyrun project: godot --headless --path game --import (finding f-30dcbb08).

.DESCRIPTION
  A fresh worktree has no game/.godot/imported cache, so the .glb models under game/assets never import and Actor
  falls back to capsules. tools/godot/screens.ps1 and tools/godot/godot.ps1 refuse such a tree in preflight with
  "PREFLIGHT missing-import-cache <n> models unimported; run tools/godot/import.ps1" (exit 6) and never import
  themselves: the import takes about two minutes and must not be absorbed by a capture's timeout cap.

  This script resolves the Godot binary and DOTNET_ROOT exactly as tools/godot/godot.ps1 does, takes the same
  machine-wide launch lock (exit 4 while another live Godot holds it) and runs under the same wall-clock cap
  (KEYRUN_GODOT_TIMEOUT_SEC). It then checks that every .glb under game/assets has an import cache file.
  Lanes never run this script; the orchestrator does.

  Output: "IMPORT done files=<n>" where n counts the files under game/.godot/imported, exit 0.
  On failure it prints "IMPORT failed <named cause>" and exits non-zero.

.PARAMETER ProjectRel
  The Godot project folder, relative to the repo root. Default game.

.NOTES
  Exit codes: 0 done; 2 Godot binary missing or KEYRUN_GODOT_TIMEOUT_SEC invalid; 4 another Godot instance holds the
  launch lock; 6 the import cache is still missing after the pass; 8 the Godot import exited non-zero;
  124 the wall-clock cap was reached and the process tree was killed.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/godot/import.ps1
#>
param(
    [string]$ProjectRel = 'game'
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

# Godot resolves a relative --path against the caller's working folder, not the repo root (finding: a foreign cwd
# failed with 'Invalid project path specified: "game"'). Hand Godot the absolute project folder.
$projectDir = $ProjectRel
if (-not [IO.Path]::IsPathRooted($ProjectRel)) { $projectDir = Join-Path $repo $ProjectRel }

# Godot binary lookup, same order as tools/godot/godot.ps1.
$wingetRel = 'Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$godot = $env:KEYRUN_GODOT
if (-not $godot -and $env:LOCALAPPDATA) {
    $godot = Join-Path $env:LOCALAPPDATA $wingetRel
}
if (-not $godot -or -not (Test-Path -LiteralPath $godot -PathType Leaf)) {
    Write-Output ("IMPORT failed godot-missing '{0}' (set KEYRUN_GODOT)" -f $godot)
    exit 2
}

# Same .NET and build-daemon environment as tools/godot/godot.ps1.
$dotnetRoot = $env:DOTNET_ROOT
if (-not $dotnetRoot -and $env:USERPROFILE) {
    $candidate = Join-Path $env:USERPROFILE '.dotnet'
    if (Test-Path -LiteralPath (Join-Path $candidate 'dotnet.exe') -PathType Leaf) {
        $dotnetRoot = $candidate
    }
}
if ($dotnetRoot) {
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = $dotnetRoot + [IO.Path]::PathSeparator + $env:PATH
}
if (-not $env:MSBUILDDISABLENODEREUSE) { $env:MSBUILDDISABLENODEREUSE = '1' }
if (-not $env:UseSharedCompilation) { $env:UseSharedCompilation = 'false' }
if (-not $env:DOTNET_CLI_USE_MSBUILD_SERVER) { $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0' }

. (Join-Path $PSScriptRoot 'launch-lock.ps1')
. (Join-Path $PSScriptRoot 'preflight.ps1')
try {
    $timeoutSec = Get-KeyrunGodotTimeoutSec
} catch {
    Write-Output ("IMPORT failed bad-timeout {0}" -f $_.Exception.Message)
    exit 2
}

# Same single-instance lock as godot.ps1: a busy lock exits 4 at once.
$launchLock = Enter-KeyrunLaunchLock -Owner 'import.ps1 --headless --import' -ExitCodeOnBusy 4
$code = $null
try {
    # Godot writes warnings to stderr; they arrive as plain output lines. The exit code is the verdict.
    $ErrorActionPreference = 'Continue'
    Invoke-KeyrunCapped -FilePath $godot -Arguments @('--headless', '--path', $projectDir, '--import') -TimeoutSec $timeoutSec -Label 'import.ps1'
    $code = $script:KeyrunLastExitCode
} finally {
    Exit-KeyrunLaunchLock $launchLock
}

if ($code -eq 124) {
    Write-Output ("IMPORT failed wall-clock-cap {0}s reached (KEYRUN_GODOT_TIMEOUT_SEC); the import was killed" -f $timeoutSec)
    exit 124
}
if ($code -ne 0) {
    Write-Output ("IMPORT failed godot-exit {0}" -f $code)
    exit 8
}

$cacheDir = Join-Path (Join-Path $projectDir '.godot') 'imported'
$files = 0
if (Test-Path -LiteralPath $cacheDir -PathType Container) {
    $files = @(Get-ChildItem -LiteralPath $cacheDir -Recurse -File).Count
}

$check = Test-KeyrunImportCache -RepoRoot $repo -ProjectRel $projectDir
if (-not $check.Ok) {
    Write-Output ("IMPORT failed import-cache-still-missing: {0}" -f $check.Lines[0])
    exit $check.Code
}

Write-Output ("IMPORT done files={0}" -f $files)
exit 0
