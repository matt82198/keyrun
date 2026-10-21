<#
.SYNOPSIS
  Headless scene test for the Seed's bonus loot on the run-end panel (lane SEED-LOOT-VIEW; BIBLE s9: the Seed elite
  "drops bonus loot"; SeedLoot.cs awards one BonusLootEntry when a Combo lands in the Seed's seedbloom Window).

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 twice and reads the run-end panel's own
  "HUD runend bonus ..." line (printed under --autorun with the other "HUD runend" lines; RunEndPanel folds
  SeedLoot.Observe over every step's events, the rule from SeedLoot.RuleFor over the run's enemy scripts):

    Run A (pull 1, the Choir, solo Warden, wipes):  -- --autorun --ticks <Ticks> --solo
      1. exit 0 and "HUD runend bonus count=0 seedWindowEnds=none": the run never meets the Seed, so the panel draws
         no "Bonus loot:" line. R-446-CI (keyrun #446, f-ea16265e): under R-BACKFILL-2 (BIBLE s9.1) the wipe is a
         release, not a run end, so the run (the same run as ScreenSceneTest) ends at the timer cap, tick 9600,
         depleted, still without meeting the Seed; <Ticks> is 9800, the cap end plus the 109 ticks the run kept past
         its old wipe end (491 of 600). Pinned headless by tools/Keyrun.Playtest.Tests/HudSceneSweepWipeReleaseTests.cs,
         which also pins run C's party run ending (timed, tick 3888) inside the unchanged <PartyTicks>.
    Run C (pull 1, the three-seat bot party, clears the dungeon at seed 1 key 2):  -- --autorun --ticks <PartyTicks>
      2. exit 0 and a seedbloom "window_opened" EVENT before run_end (the Seed died and opened its Window);
      3. "HUD runend bonus count=K seedWindowEnds=E": E is that EVENT's endsTick, K is 1 when a seedbloom "combo"
         EVENT at or before E precedes run_end and 0 otherwise, and the line ends ' label="Bonus loot: ..."' (the
         line the panel draws under the result) exactly when K is 1. At seed 1 key 2 the bots let the Window expire,
         so K is 0 and the panel draws no bonus line; the combo branch of the fold is SeedLootTests (xUnit).
    Run P (lane SEED-BONUS-REWARDS, the probe scene res://src/Hud/RunEndRewardsProbe.tscn):
      4. exit 0 and "RUNEND-REWARDS PASS cases=3": with the run's Rewards carrying the Seed award or not, and the
         panel's log fed the matching events, no events, or events that would fold to an award the run does not
         carry, the panel's "HUD runend bonus" line is the one Run.Rewards gives (the sim's award, #197).
  Before the lane the panel prints no "HUD runend bonus" line: 1 and 3 fail (RED). Before SEED-BONUS-REWARDS the
  panel re-folds SeedLoot.Observe over the events itself: 4 fails on the carried and none cases (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Hud/RunEndBonusSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 9800,
    [int]$PartyTicks = 9000  # R-DOOR-1: the door at 100% makes the dungeon longer by design (was 7000)
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

# One launch lock for all three launches (tools/godot/launch-lock.ps1); they run one after another. Exit 2 when
# another Godot instance holds the lock.
. (Join-Path $repo 'tools/godot/launch-lock.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner 'RunEndBonusSceneTest.ps1' -ExitCodeOnBusy 2
try {

function Invoke-Arena([int]$n, [string[]]$extra) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $n) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $n @extra 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') {
        Write-Output $text
        Write-Output 'RunEndBonusSceneTest: CANNOT RUN (Godot binary not found)'
        exit 2
    }
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}

function First($lines, [string]$pattern) {
    return $lines | Where-Object { $_ -match $pattern } | Select-Object -First 1
}

# ---- Run A: the solo wipe in pull 1 never meets the Seed --------------------------------------------------------
$a = Invoke-Arena $Ticks @('--solo')
if ($a.Code -ne 0) { $failures.Add("run A: godot exited $($a.Code) (expected 0)") }
$bonusA = First $a.Lines '^HUD runend bonus '
if ($bonusA -ne 'HUD runend bonus count=0 seedWindowEnds=none') {
    $failures.Add("run A: want 'HUD runend bonus count=0 seedWindowEnds=none' (no Seed met, no bonus loot), got '$bonusA'")
}

# ---- Run C: the party kills the Seed on the way to the boss -----------------------------------------------------
$c = Invoke-Arena $PartyTicks @()
if ($c.Code -ne 0) { $failures.Add("run C: godot exited $($c.Code) (expected 0)") }
$runEndEv = First $c.Lines '^EVENT \{.*"type":"run_end"'
$iRunEnd = if ($runEndEv) { [array]::IndexOf($c.Lines, $runEndEv) } else { $c.Lines.Count }
$cLive = @($c.Lines[0..([Math]::Max(0, $iRunEnd - 1))])
$bonusC = First $c.Lines '^HUD runend bonus '
$seedOpen = First $cLive '^EVENT \{"endsTick":\d+,.*"type":"window_opened","windowId":"seedbloom"\}$'
$seedCombo = $null
$seedEnds = $null
if (-not $seedOpen) {
    $failures.Add('run C: the Seed''s seedbloom Window never opened before run_end (no window_opened EVENT), so 3 cannot run')
} elseif (-not $bonusC) {
    $failures.Add('run C: no "HUD runend bonus" line (the run-end panel does not report the Seed''s bonus loot)')
} else {
    $seedEnds = [int][regex]::Match($seedOpen, '"endsTick":(\d+)').Groups[1].Value
    $seedCombo = $cLive | Where-Object {
        $_ -match '^EVENT \{.*"tick":(\d+),"type":"combo","windowId":"seedbloom"\}$' -and [int]$Matches[1] -le $seedEnds
    } | Select-Object -First 1
    $wantBonus = if ($seedCombo) {
        "^HUD runend bonus count=1 seedWindowEnds=$seedEnds label=`"Bonus loot: [^`"]+`"$"
    } else {
        "^HUD runend bonus count=0 seedWindowEnds=$seedEnds$"
    }
    if ($bonusC -notmatch $wantBonus) {
        $failures.Add("run C: want a bonus line matching '$wantBonus', got '$bonusC'")
    }
}

# ---- Run P (lane SEED-BONUS-REWARDS): the panel's bonus line follows Run.Rewards, never its own fold ------------
# The probe scene (RunEndRewardsProbe.cs) ends a real content run three ways (agree, carried, none) and checks the
# "HUD runend bonus" line against the run's Rewards. Silent on PASS so the lines above and the PASS line are unchanged.
$ErrorActionPreference = 'Continue'
$pText = & $wrapper --headless --path (Join-Path $repo 'game') --quit-after 600 'res://src/Hud/RunEndRewardsProbe.tscn' 2>&1 | Out-String
$pCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$pLines = @($pText -split "`r?`n")
if ($pCode -ne 0) { $failures.Add("run P: godot exited $pCode (expected 0)") }
foreach ($l in @($pLines | Where-Object { $_ -match '^RUNEND-REWARDS FAIL ' })) { $failures.Add("run P: $l") }
if (-not (First $pLines '^RUNEND-REWARDS PASS cases=3$')) {
    $failures.Add('run P: no "RUNEND-REWARDS PASS cases=3" line (the panel bonus line does not follow Run.Rewards)')
}

Write-Output '--- asserted lines ---'
foreach ($l in @($bonusA, $seedOpen, $seedCombo, $bonusC)) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "RunEndBonusSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "RunEndBonusSceneTest: PASS ticks=$Ticks partyTicks=$PartyTicks seedWindowEnds=$seedEnds seedCombo=$(if ($seedCombo) { 1 } else { 0 })"
exit 0
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
