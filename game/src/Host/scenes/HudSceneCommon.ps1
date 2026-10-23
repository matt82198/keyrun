<#
.SYNOPSIS
  Shared helpers of the per-run HUD scene tests in this folder (HudRun<X>-SceneTest.ps1), split from the former
  game/src/Host/HudSceneTest.ps1 monolith (module-boundary review slice S3). Not a scene test itself: the CI glob is
  game/src/**/*SceneTest.ps1 and this file does not match it.

.DESCRIPTION
  Dot-source it from a scene test after its param block and after setting $SceneName (the test's base name):
      $SceneName = 'HudRunA-SceneTest'
      . (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
  It sets $repo and $failures in the caller's scope, loads tools/godot/launch-lock.ps1, and defines the line helpers
  the runs share. Every failures.Add of a helper lives here exactly once, so the split carries the monolith's assertion
  strings one for one (README.md, "Assertion count"). It never launches: each run file holds its own launch function
  (& $wrapper) and its own launch lock, so tools/godot/launch-lock-test.ps1 case G parses and lock-checks every launch.
#>
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$failures = New-Object System.Collections.Generic.List[string]

# One launch lock per scene test (tools/godot/launch-lock.ps1): no other launch can interleave with this test's
# launches. Exit 2 when another Godot instance holds the lock. The caller takes it with Enter-KeyrunLaunchLock.
. (Join-Path $repo 'tools/godot/launch-lock.ps1')

# The scene log: the engine output each launch printed, one "=== <launch> ===" block per launch, written to
# <KEYRUN_SCENE_LOG_DIR or <repo>/scene-logs>/<Name>.engine.log (*.log is git-ignored). CI uploads scene-logs/ on failure.
$sceneLogDir = if ($env:KEYRUN_SCENE_LOG_DIR) { $env:KEYRUN_SCENE_LOG_DIR } else { Join-Path $repo 'scene-logs' }
$sceneLogPath = Join-Path $sceneLogDir "$SceneName.engine.log"
if (Test-Path -LiteralPath $sceneLogPath) { Remove-Item -LiteralPath $sceneLogPath }

function Write-SceneLog([string]$launch, $lines) {
    New-Item -ItemType Directory -Force -Path $sceneLogDir | Out-Null
    $text = "=== $launch ===`n" + (@($lines) -join "`n") + "`n"
    [IO.File]::AppendAllText($sceneLogPath, $text, (New-Object System.Text.UTF8Encoding($false)))
}

# Exit 2 (could not run) when a launch found no Godot binary (tools/godot/godot.ps1 exits 2 and says so).
function Assert-GodotFound([int]$rc, [string]$text) {
    if ($rc -eq 2 -and $text -match 'Godot binary not found') {
        Write-Output $text
        Write-Output "${SceneName}: CANNOT RUN (Godot binary not found)"
        exit 2
    }
}

function First($lines, [string]$pattern) {
    return $lines | Where-Object { $_ -match $pattern } | Select-Object -First 1
}

# Lines of the first run only: everything before a second "RUN start" (the screen's autorun script queues the next
# run once the first ends, lane 3I, so a long autorun can hold two runs).
function FirstRun($lines) {
    $starts = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^RUN start ') { $i } })
    if ($starts.Count -lt 2) { return $lines }
    return $lines[0..($starts[1] - 1)]
}

# "BOSS armed twins=2 lockAt=50" before the run's first TICK line (lane 3I).
function Assert-BossArmed([string]$run, $lines) {
    $armed = First $lines '^BOSS armed twins=2 lockAt=50\b'
    $tick = First $lines '^TICK '
    if (-not $armed) {
        $failures.Add("run ${run}: no 'BOSS armed twins=2 lockAt=50' line (the run's boss is not armed)")
    } elseif ($tick -and [array]::IndexOf($lines, $armed) -gt [array]::IndexOf($lines, $tick)) {
        $failures.Add("run ${run}: 'BOSS armed' came after the first TICK (the boss was armed mid-run)")
    }
    return $armed
}

# The "startHeading" header value of an input log file, as its text, or $null (lane HOST-HEADING; runs K and M).
function Get-StartHeadingText([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $hit = [regex]::Match([IO.File]::ReadAllText($path), '(?m)^  "startHeading": ([^,\r\n]+),$')
    if ($hit.Success) { return $hit.Groups[1].Value }
    return $null
}

# The weekly affix rotation (AffixWeek.cs): every recording carries a "rotationIndex" header line, the index the run was
# built with (this reset week's, Tuesday 15:00 UTC, or --rotation N's on a live run, the replayed log's on a replay, 0 for a log that has none). The committed
# examples predate the field, so the byte comparisons leave that one line out and assert it separately (runs K and L).
function Get-RotationIndexText([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $hit = [regex]::Match([IO.File]::ReadAllText($path), '(?m)^  "rotationIndex": ([0-9]+),$')
    if ($hit.Success) { return $hit.Groups[1].Value }
    return $null
}
function Remove-RotationIndexLine([string]$text) { return $text -replace '(?m)^  "rotationIndex": [0-9]+,\n', '' }
# Scene anchors are derived headlessly at rotation 0, so every autorun scene launch pins --rotation 0: a live run takes the week's
# index from the UTC clock (SimDriver.cs:240-245, AffixWeek.ForRun), which made a scene result depend on the week it ran in.

# ---- Run-end titles (lane 3C-d), applied to each autorun's own lines (runs A-G) -----------------------------------
# A cleared run keeps the clear title; an uncleared depleted run is the timer cap's, "Out of time" (R-BACKFILL-2, BIBLE
# s9.1, f-47a7a6db; the #446 lane brief's R-WIPE-RESET: every host run is a layout run, where a wipe is a release and a
# run-back, never a run end, so no title may say "Party wiped"; RunEndTitle.ForRun). Run C (and its repeat) clears the
# dungeon since lane TUNE-BOSS; runs A and E end at the timer cap; the others time out of ticks: Cleared says
# how many cleared. -RequireTitle (run A) keeps the monolith's "the title checks did not run" guard: run A always
# prints a run-end line, so a run with none fails it.
function Assert-RunEndTitles($lines, [switch]$RequireTitle) {
    $titleLines = @(@($lines) | Where-Object { $_ -match '^HUD runend result=' })
    $clearedTitles = 0
    foreach ($t in $titleLines) {
        if ($t -match ' cleared=1 ') {
            $clearedTitles++
            if ($t -notmatch ' title="Run cleared -- (within par|over par|key stays at \+\d+)"$') { $failures.Add("a cleared run lost the clear title: '$t'") }
            # RunEndTitle.ForRun (one source with the sim): "within par"/"over par" only for a timed run, "key stays at +N"
            # only for a depleted (untimed) one.
            if ($t -match ' title="Run cleared -- (within par|over par)"$' -and $t -notmatch '^HUD runend result=timed ') { $failures.Add("a non-timed clear has a timed title: '$t'") }
            if ($t -match ' title="Run cleared -- key stays at \+\d+"$' -and $t -notmatch '^HUD runend result=depleted ') { $failures.Add("a timed clear is titled as an untimed key: '$t'") }
        } elseif ($t -match '^HUD runend result=depleted ' -and $t -notmatch ' title="Out of time -- key stays at \+\d+"$') {
            $failures.Add("an uncleared depleted run's title is not the timer cap's 'Out of time -- key stays at +<key>' (R-BACKFILL-2): '$t'")
        }
        if ($t -match ' title="Party wiped ') { $failures.Add("a run-end title says the party wiped, but a wipe no longer ends a run (R-BACKFILL-2): '$t'") }
        # An untimed key keeps its level (Matt 2026-10-20, f-7c7abdf4): no title may say "depleted", which a WoW player
        # reads as the key losing a level.
        if ($t -match ' title="[^"]*depleted') { $failures.Add("a run-end title says the key is depleted: '$t'") }
    }
    if ($RequireTitle -and $titleLines.Count -eq 0) { $failures.Add('no "HUD runend result=" line in any autorun (the title checks did not run)') }
    return [pscustomobject]@{ Lines = $titleLines; Cleared = $clearedTitles }
}

function Write-RunEndTitles($titles) {
    Write-Output "--- run-end titles, lane 3C-d (lines=$($titles.Lines.Count) cleared=$($titles.Cleared)) ---"
    foreach ($l in $titles.Lines) { Write-Output $l }
}

# Lane COMBO-BLAME (BIBLE s4): the run-end panel's late-combo lines are seat 1's own only, one per distinct
# (windowId, reason) of the combo_denied EVENTs whose payoffActorId is 1, capped at 3 (runs A and C).
function Assert-ComboBlame([string]$Name, $Lines) {
    $run = @{ Name = $Name; Lines = $Lines }
    $lateLines = @($run.Lines | Where-Object { $_ -match '^HUD runend combo ' })
    $lateForeign = @($lateLines | Where-Object { $_ -notmatch '^HUD runend combo seat=1 window=\S* reason=\S* half=payoff count=[1-9]\d*$' })
    if ($lateForeign.Count -gt 0) { $failures.Add("run $($run.Name): the run-end panel shows a late-combo line that is not seat 1's own: '$($lateForeign[0])'") }
    $ownDenied = @($run.Lines | Where-Object { $_ -match '^EVENT \{.*"payoffActorId":1,.*"type":"combo_denied"' } |
        ForEach-Object { $w = if ($_ -match '"windowId":"([^"]*)"') { $Matches[1] } else { '' }; $r = if ($_ -match '"reason":"([^"]*)"') { $Matches[1] } else { '' }; "$w|$r" } |
        Sort-Object -Unique)
    $expectLate = [Math]::Min(3, $ownDenied.Count)
    if ($lateLines.Count -ne $expectLate) { $failures.Add("run $($run.Name): expected $expectLate 'HUD runend combo' line(s) for seat 1's $($ownDenied.Count) distinct combo_denied, got $($lateLines.Count)") }
    Write-Host "COMBO-BLAME run $($run.Name): seat1 denied distinct=$($ownDenied.Count) lines=$($lateLines.Count)"
}

# Prints "<Name>: FAIL (n)" and the failure lines (exit 1), or "<Name>: PASS <detail>" (exit 0).
function Complete-Scene([string]$detail) {
    if ($failures.Count -gt 0) {
        Write-Output "${SceneName}: FAIL ($($failures.Count))"
        $failures | ForEach-Object { Write-Output "  - $_" }
        exit 1
    }
    Write-Output "${SceneName}: PASS $detail"
    exit 0
}
