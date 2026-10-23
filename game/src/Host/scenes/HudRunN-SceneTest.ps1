<#
.SYNOPSIS
  HUD scene run N (check 41, lane TINT-RUNOVER): a non-clear run end with a living out-of-range target; three-seat
  party, key 2.
  -- --autorun --ticks <TintTicks> --abandon-seat <TintAbandon>   (no --joiner-key; lane S1, R-K4-13 (c))

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text, the abandon
  and tick defaults' history and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunN-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    # Lane S1 (R-K4-13 (c)): the seat abandon at 2@300 ends depleted at 6900 with no far living hostile while live, so the
    # no-abandon run was used. 2@2400 (seed 1, key 2, bots on) ends depleted at 11279 with a living hostile 3651 mm away
    # at tick 1468 (before the abandon fires), and is the smallest anchor change found that does both. 2@900 has no far
    # hostile; 2@1500 has one but ends depleted only at 22300 (measured before the lane S1 Forces parity change). Derivation:
    # HudRunOverTintTests Candidates[0].
    # Lane S1 premise (scratchpad/lanes/s1-hudrunN-premise.log): key 2 clears hollow-vault on this tree, so the scene now
    # passes --key 3 (ArenaOptions.cs:194-196, SimDriver.cs:245). Headless seed 1, key 3, abandon 2@2400, bots on, the scene's
    # Forces plan (SimDriver.cs:254-258): ResultTick 12900 = CapTicks 26200 - PenaltyTicks 13300, Depleted, uncleared, at the
    # timer cap. 13100 = 12900 + 200, a 200-tick margin past that end. Derivation: HudRunOverTintTests Run_N_ends_depleted_*.
    [int]$TintTicks = 13100,
    [string]$TintAbandon = '2@2400'
)
$SceneName = 'HudRunN-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

function Invoke-Arena([int]$n, [string[]]$extra) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $n) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $n --rotation 0 @extra 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Assert-GodotFound $rc $text
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}

# ---- Run N (lane TINT-RUNOVER, check 41): the range tint is off once a non-clear run has ended -------------------
# --key 3: lane S1 premise (s1-hudrunN-premise.log); key 2 clears on this tree, key 3 ends at the timer cap.
$tintN = Invoke-Arena $TintTicks @('--key', '3', '--abandon-seat', $TintAbandon)
Write-SceneLog 'N' $tintN.Lines
$tintRun = @(FirstRun $tintN.Lines)
if ($tintN.Code -ne 0) { $failures.Add("run N: godot exited $($tintN.Code) (expected 0)") }
$tintEnd = First $tintRun '^HUD runend result=depleted .*\bcleared=0 '
$tintLive = First $tintRun '^HUD bar range tinted=[1-9]\d* over=0 '
$tintOver = @($tintRun | Where-Object { $_ -match '^HUD bar range tinted=\d+ over=1 ' })
$tintStuck = $tintOver | Where-Object { $_ -notmatch '^HUD bar range tinted=0 over=1 ' } | Select-Object -First 1
if (-not $tintEnd) { $failures.Add("run N: no 'HUD runend result=depleted ... cleared=0' line (the run must end uncleared for the case: the timer cap's end since R-BACKFILL-2; got '$(First $tintRun '^HUD runend result=')')") }
if (-not $tintLive) { $failures.Add('run N: no "HUD bar range tinted=N over=0" line with N > 0 (control: a live run with an out-of-range target tints)') }
if ($tintOver.Count -eq 0) { $failures.Add('run N: no "HUD bar range ... over=1" line (the bar never saw the run end)') }
if ($tintStuck) { $failures.Add("run N: the range tint still shows after the run ended: '$tintStuck'") }

Write-Output '--- run N (range tint after a non-clear run end), lane TINT-RUNOVER ---'
foreach ($l in @($tintLive, $tintEnd) + $tintOver) {
    if ($l) { Write-Output $l }
}

Complete-Scene "tintTicks=$TintTicks"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
