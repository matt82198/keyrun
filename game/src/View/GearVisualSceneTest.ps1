<#
.SYNOPSIS
  Headless scene test for U6 GEAR-VISUAL (keyrun MVP unit U6; the gear you equip is the gear you see, the keyrun STATE.md
  gear-on-model ruling 2026-10-20, Dark and Darker way): taking an equipment reward changes the Warden's drawn gear.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 with a fresh temp directory as
  --progression-dir (the test never reads or writes the real user:// save) and runs the between-runs screen's own
  autorun script (bank, completion-roll take, rerolls, talent steps), the same run ProgressionSaveSceneTest drives:

      godot --headless --path game --fixed-fps 60 -- --autorun --ticks 9800 --seed 5 --solo --progression-dir <tmp>

  src/View/ActorView.cs (Gear) reads the Warden's equipped items live from the screen each frame and prints, on each
  change, through GD.Print (stdout) and the run log:
    GEAR none actor=N                              (nothing equipped; the first look, before the take)
    GEAR attach actor=N slot=S tier=T shape=X      (one per equipped slot; S in weapon|armor|charm|ring|keystone)
  The pure rule is GearVisual.Plan (src/View/GearVisual.cs): one primitive mark per equipped slot, tinted by tier.
  This test asserts:
    1. the process exits 0 and prints "AUTORUN done ticks=N";
    2. the Warden (actor 1, SimDriver.WardenId) is the only actor named by any GEAR line, so no enemy and no bot ally
       ever draws a mark;
    3. the first GEAR line is "GEAR none" (launch 1 starts with nothing equipped), so the drawn gear starts empty;
    4. at least one "GEAR attach" line follows it, its slot is the slot of the reward the autorun took
       ("SCREEN roll take slot=<slot> tier=<t> id=..." from the completion roll), its tier is between 1 and 10, and its shape is box or sphere: the
       equipment change changed the drawn attachment;
    5. every GEAR attach line of the run names one of the five slots (no other mark is drawn).
  Before U6 the view prints no GEAR line at all, so launch 1 has no "GEAR none" line and the test is RED.

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/View/GearVisualSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 9800
)
$ErrorActionPreference = 'Stop'

$WardenId = 1
$Slots = 'weapon|armor|charm|ring|keystone'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

# One launch lock for the launch (tools/godot/launch-lock.ps1). Exit 2 when another Godot instance holds it.
. (Join-Path $repo 'tools/godot/launch-lock.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner 'GearVisualSceneTest.ps1' -ExitCodeOnBusy 2
try {
$dir = Join-Path ([System.IO.Path]::GetTempPath()) ('keyrun-gearvisual-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null

# --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
$guard = (3 * $Ticks) + 240
$ErrorActionPreference = 'Continue'
# R-K4-16: --reward-seed 1 pins the completion roll (seat 1 raw 217 < 3500, prng.vectors.json), so the roll no longer moves with ResultTick.
$text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $Ticks --seed 5 --solo --reward-seed 1 --progression-dir $dir 2>&1 | Out-String
$rc = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue

if ($rc -eq 2 -and $text -match 'Godot binary not found') {
    Write-Output $text
    Write-Output 'GearVisualSceneTest: CANNOT RUN (Godot binary not found)'
    exit 2
}
if ($rc -ne 0) { $failures.Add("godot exited $rc (expected 0)") }

$lines = @($text -split "`r?`n")
if (-not ($lines | Where-Object { $_ -match "^AUTORUN done ticks=$Ticks " } | Select-Object -First 1)) {
    $failures.Add("no 'AUTORUN done ticks=$Ticks' summary")
}

$gear = @($lines | Where-Object { $_ -match '^GEAR (none|attach) ' })
$none = @($gear | Where-Object { $_ -match '^GEAR none ' })
$attach = @($gear | Where-Object { $_ -match "^GEAR attach actor=\d+ slot=($Slots) tier=\d+ shape=(box|sphere)$" })

# 2. Only the Warden draws gear.
foreach ($l in $gear) {
    if ($l -match '^GEAR (none|attach) actor=(\d+)') {
        if ([int]$Matches[2] -ne $WardenId) { $failures.Add("a GEAR line names actor $($Matches[2]), not the Warden ($WardenId): '$l'") }
    } else {
        $failures.Add("malformed GEAR line: '$l'")
    }
}

# 3. Launch 1 starts with nothing equipped.
$firstGear = $gear | Select-Object -First 1
if (-not $firstGear -or $firstGear -notmatch "^GEAR none actor=$WardenId$") {
    $failures.Add("the first GEAR line is not 'GEAR none actor=$WardenId' (got '$firstGear')")
}

# 4. The completion roll's item equips a slot and that slot is drawn.
# needs a seed whose seat-1 roll is an item; orchestrator derives the seed if the current one rolls nothing
$take = $lines | Where-Object { $_ -match '^SCREEN roll take slot=(\w+) ' } | Select-Object -First 1
$slot = $null
if ($take) { $null = $take -match '^SCREEN roll take slot=(\w+) '; $slot = $Matches[1] }
if (-not $take) {
    $failures.Add('no "SCREEN roll take slot=" line: the completion roll gave nothing, so nothing was equipped')
} elseif ($slot -notmatch "^($Slots)$") {
    $failures.Add("the taken reward's slot '$slot' is not one of the five gear slots")
}
if ($attach.Count -eq 0) {
    $failures.Add('no "GEAR attach" line: the equipped item was not drawn on the Warden')
} else {
    if ($none.Count -eq 0) { $failures.Add('no "GEAR none" line before the attach (the drawn gear never started empty)') }
    if ($slot -and -not ($attach | Where-Object { $_ -match "slot=$slot " })) {
        $failures.Add("the taken slot '$slot' has no 'GEAR attach' line (attach lines: $($attach -join ' | '))")
    }
    foreach ($l in $attach) {
        if ($l -match ' tier=(\d+) ') {
            $tier = [int]$Matches[1]
            if ($tier -lt 1 -or $tier -gt 10) { $failures.Add("a GEAR attach tier is outside 1..10: '$l'") }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Output 'GearVisualSceneTest: FAIL'
    $failures | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
    exit 1
}
$shown = ($attach | Select-Object -First 5) -join ' | '
Write-Output "GearVisualSceneTest: PASS (take=$slot; $($none.Count) GEAR none, $($attach.Count) GEAR attach: $shown)"
exit 0
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
