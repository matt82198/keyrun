<#
.SYNOPSIS
  HUD scene run H (check 27, lane 3D-b): the interactive launch opens the screen first; NO --autorun, three-seat party.
  -- (no user args), --quit-after <ScreenFirstFrames>

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text and the RED
  notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunH-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$ScreenFirstFrames = 240
)
$SceneName = 'HudRunH-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

# ---- Run H: the interactive launch opens the screen first (lane 3D-b) ----------------------------------------------
$ErrorActionPreference = 'Continue'
$hText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $ScreenFirstFrames 2>&1 | Out-String
$hCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $hCode $hText
$hLines = @($hText -split "`r?`n")
Write-SceneLog 'H' $hLines
if ($hCode -ne 0) { $failures.Add("run H: godot exited $hCode (expected 0)") }
$firstH = First $hLines '^SCREEN first open=1 key=2$'
if (-not $firstH) { $failures.Add("run H: no 'SCREEN first open=1 key=2' line (the launch did not open the between-runs screen first)") }
$startH = First $hLines '^(RUN start|BOSS armed|PULL spawn|ARENA error) '
if ($startH) { $failures.Add("run H: a run started (or failed) before any queue action: '$startH'") }

Write-Output '--- run H (screen first), lane 3D-b ---'
foreach ($l in @($firstH, $startH)) {
    if ($l) { Write-Output $l }
}

Complete-Scene "screenFirstFrames=$ScreenFirstFrames"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
