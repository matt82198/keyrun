<#
.SYNOPSIS
  HUD scene run G (check 24, lane 3J): key mismatch, as run F with --joiner-key 3.
  -- --autorun --ticks <BackfillTicks> --abandon-seat <AbandonSeat> --joiner-key 3

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text and the RED
  notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunG-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$BackfillTicks = 3500,
    [string]$AbandonSeat = '2@pull0.hp10+1'
)
$SceneName = 'HudRunG-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

function Invoke-Arena([int]$n, [string[]]$extra) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $n) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $n @extra 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Assert-GodotFound $rc $text
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}
. (Join-Path $PSScriptRoot 'HudBackfillCommon.ps1')

$g = Invoke-Backfill 'G' 3
if ($g.Refill) { $failures.Add("run G: a key-3 joiner refilled a key-2 run: '$($g.Refill)'") }
if ($g.Consume) { $failures.Add("run G: the key-3 joiner was consumed: '$($g.Consume)'") }

# Run-end titles (lane 3C-d) on run G's lines.
$titlesG = Assert-RunEndTitles $g.Lines

Write-Output '--- run G (backfill key mismatch), lane 3J ---'
foreach ($l in @($g.Abandon, $g.Add, $g.Pull)) {
    if ($l) { Write-Output $l }
}
Write-RunEndTitles $titlesG

Complete-Scene "backfillTicks=$BackfillTicks abandonSeat=$AbandonSeat"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
