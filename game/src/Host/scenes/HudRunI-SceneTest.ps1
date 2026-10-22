<#
.SYNOPSIS
  HUD scene run I (check 28, lane 3D-c): Enter after a wipe opens the screen, never a Clause-less restart; interactive,
  three-seat party, from pull 2.
  -- --press-enter 3 --pull 2 --bots-hold   (--quit-after <WipeFrames>)

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text, the frame
  budget's history and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunI-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$WipeFrames = 41200
)
$SceneName = 'HudRunI-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

# ---- Run I: Enter after a wipe opens the screen, never a Clause-less restart (lane 3D-c) --------------------------
$ErrorActionPreference = 'Continue'
$iText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $WipeFrames '--' --press-enter 3 --pull 2 --bots-hold 2>&1 | Out-String
$iCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Assert-GodotFound $iCode $iText
$iLines = @($iText -split "`r?`n")
Write-SceneLog 'I' $iLines
if ($iCode -ne 0) { $failures.Add("run I: godot exited $iCode (expected 0)") }
$firstI = First $iLines '^SCREEN first open=1 key=2$'
if (-not $firstI) { $failures.Add("run I: no 'SCREEN first open=1 key=2' line") }
$startsI = @($iLines | Where-Object { $_ -match '^RUN start ' })
$runEndI = First $iLines '^EVENT \{"result":"depleted",.*"type":"run_end"'
$reopenI = $null
$requeuedI = $null
# R1 (BIBLE s8): key 2 is below run.json clauseStartKey 5, so the screen's Queue hands the run no Clause (clause=none).
if ($startsI.Count -lt 1 -or $startsI[0] -notmatch ' key=2 clause=none ') {
    $failures.Add("run I: the first run did not come from the screen's Queue at key 2 with no Clause: '$($startsI | Select-Object -First 1)'")
}
if (-not $runEndI) {
    $failures.Add("run I: the run did not end depleted (no depleted run_end EVENT; the timer cap ends it since R-BACKFILL-2) in $WipeFrames frames")
} else {
    $iEnd = [array]::IndexOf($iLines, $runEndI)
    $afterEnd = @($iLines[($iEnd + 1)..($iLines.Count - 1)])
    $nextI = First $afterEnd '^(RUN start|SCREEN reopen) '
    $press3 = First $afterEnd '^INPUT enter n=3 screen=1$'
    if (-not $nextI) {
        $failures.Add('run I: Enter after the wipe neither reopened the screen nor started a run (no "SCREEN reopen" line)')
    } elseif ($nextI -match '^RUN start ') {
        $failures.Add("run I: Enter after the wipe started a run without the screen: '$nextI'")
    } elseif ($nextI -notmatch '^SCREEN reopen result=depleted open=1 key=2$') {
        $failures.Add("run I: wrong reopen line: '$nextI' (want 'SCREEN reopen result=depleted open=1 key=2')")
    } else {
        $reopenI = $nextI
        $iRe = [array]::IndexOf($afterEnd, $reopenI)
        $afterRe = @($afterEnd[($iRe + 1)..($afterEnd.Count - 1)])
        $requeuedI = First $afterRe '^RUN start '
        if (-not $press3) {
            $failures.Add('run I: no third Enter on the open screen ("INPUT enter n=3 screen=1")')
        } elseif ($requeuedI -and [array]::IndexOf($afterRe, $requeuedI) -lt [array]::IndexOf($afterRe, $press3)) {
            $failures.Add("run I: a run started while the reopened screen waited, before its Queue: '$requeuedI'")
        } elseif (-not $requeuedI) {
            $failures.Add("run I: the reopened screen's Queue started no run")
        } elseif ($requeuedI -notmatch ' key=2 clause=none ') {
            # R1: the reopened screen queues key 2, below run.json clauseStartKey 5, so the run carries clause=none.
            $failures.Add("run I: the run after the wipe is not the screen's key-2 Queue with no Clause: '$requeuedI'")
        }
    }
}

Write-Output '--- run I (wipe -> screen), lane 3D-c ---'
foreach ($l in @($firstI) + $startsI + @($runEndI, $reopenI, $requeuedI)) {
    if ($l) { Write-Output $l }
}

Complete-Scene "wipeFrames=$WipeFrames"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
