<#
.SYNOPSIS
  HUD scene run B (checks 7 and 8, R-PILE-10): from pull 2, the Pair, solo Warden.
  -- --autorun --ticks <PingTicks> --pull 2 --solo

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text, the tick
  budget's history and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunB-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$PingTicks = 900
)
$SceneName = 'HudRunB-SceneTest'
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

# ---- Run B: from pull 2 -----------------------------------------------------------------------------------------
$b = Invoke-Arena $PingTicks @('--pull', '2', '--solo')
Write-SceneLog 'B' $b.Lines
if ($b.Code -ne 0) { $failures.Add("run B: godot exited $($b.Code) (expected 0)") }
$objB = First $b.Lines '^HUD objective pull=2/4 id=pair name="The Pair"'
if (-not $objB) { $failures.Add('run B: no "HUD objective pull=2/4 id=pair name=""The Pair""" line') }
# Check 8, R-PILE-10 (header): a call is made only while the Warden lives, and his death drops the mechanic.
$ping = First $b.Lines '^HUD ping label=(SPREAD|STACK) '
$deathB = $null
$deathTickB = -1
$iDeathB = -1
$tickB = -1
$pingsB = @()
for ($i = 0; $i -lt $b.Lines.Count; $i++) {
    $l = $b.Lines[$i]
    if ($l -match '^TICK (\d+) ') { $tickB = [int]$Matches[1] }
    elseif ($l -match '^HUD ping label=(SPREAD|STACK) ') { $pingsB += , @($tickB, $i, $l) }
    elseif (-not $deathB -and $l -match '^EVENT \{"actorId":1,.*"type":"death"' -and $l -match '"tick":(\d+)') {
        $deathB = $l; $deathTickB = [int]$Matches[1]; $iDeathB = $i
    }
}
$check8B = $null
$respawnB = $null
$respawnTickB = -1
if (-not $deathB) {
    $failures.Add('run B: no EVENT death line for actor 1 (the solo Warden never died; check 8 reads his death tick from it)')
} else {
    # R-BACKFILL-2 (BIBLE s9.1, f-47a7a6db; the #446 lane brief's R-WIPE-RESET: a wipe is a release and a run-back, not a
    # run end): the dead Warden respawns RunWiring.ReleaseDelayTicks (100) after his death step. "After the death" is his
    # time dead, up to and including the step of his respawn EVENT (the next TICK line ends it), as HudSceneRunsBKTests
    # walks it; once he has respawned he is a living target and the Pair's calls on him are legitimate again.
    $iDeadEndB = $b.Lines.Count
    for ($i = $iDeathB + 1; $i -lt $b.Lines.Count; $i++) {
        if (-not $respawnB) {
            if ($b.Lines[$i] -match '^EVENT \{"actorId":1,"tick":(\d+),"type":"respawn"\}$') { $respawnB = $b.Lines[$i]; $respawnTickB = [int]$Matches[1] }
        } elseif ($b.Lines[$i] -match '^TICK ') { $iDeadEndB = $i; break }
    }
    if (-not $respawnB) { $failures.Add("run B: no respawn EVENT for actor 1 after his death at $deathTickB (R-BACKFILL-2: the dead Warden releases and respawns)") }
    elseif ($respawnTickB -ne $deathTickB + 1 + 100) { $failures.Add("run B: the Warden respawned at $respawnTickB, not death $deathTickB + 1 + RunWiring.ReleaseDelayTicks 100: '$respawnB'") }
    $pingsLiveB = @()
    $pingsBeforeDeathB = 0
    foreach ($p in $pingsB) {
        if ($p[1] -lt $iDeadEndB) { $pingsLiveB += , $p; if ($p[0] -lt $deathTickB) { $pingsBeforeDeathB++ } }
    }
    # Re-pointed (check 8 (b)): no 'HUD ping' before the death is REQUIRED here. No Pair mark lands in this solo pull before
    # the Warden dies, and the pings now come only from landed marks; the live call is required in run C (check 11) and
    # by E2eAssert.ps1 (bot run). Pings at or after the death still fail below. $pingsBeforeDeathB is reported in $check8B.
    foreach ($p in $pingsLiveB) {
        if ($p[0] -ge $deathTickB) { $failures.Add("run B: a SPREAD/STACK call in the step of tick $($p[0]), not before the Warden's death at ${deathTickB}: '$($p[2])'") }
    }
    $drawnB = 0; $hitB = 0; $cancelPriorB = 0; $cancelAtB = 0; $pairTelB = 0; $drawnAfterB = 0
    foreach ($l in $b.Lines) {
        if ($l -notmatch '^EVENT \{.*"tick":(\d+),') { continue }
        $t = [int]$Matches[1]
        if ($l -match '"type":"telegraph"') {
            # Re-pointed (check 8 (c)): a Pair caster circle must never be drawn, at any tick; a telegraph after the death is a leak.
            if ($l -match '"cast":"pair\.') { $pairTelB++ }
            if ($t -le $deathTickB) { $drawnB++ } else { $drawnAfterB++ }
        }
        elseif ($l -match '"type":"telegraph_hit"' -and $t -le $deathTickB) { $hitB++ }
        elseif ($l -match '"type":"telegraph_cancel"') { if ($t -lt $deathTickB) { $cancelPriorB++ } elseif ($t -eq $deathTickB) { $cancelAtB++ } }
    }
    $liveB = $drawnB - $hitB - $cancelPriorB
    $deathStepB = First $b.Lines "^TICK $deathTickB "
    $check8B = "check 8 (R-PILE-10): death tick=$deathTickB respawn tick=$respawnTickB pings=$($pingsLiveB.Count) (pings before death=$pingsBeforeDeathB) pair telegraphs=$pairTelB telegraphs drawn=$drawnB hit=$hitB cancelled before=$cancelPriorB live=$liveB cancelled at death=$cancelAtB drawn after death=$drawnAfterB"
    if ($pairTelB -gt 0) { $failures.Add("run B: $pairTelB Pair telegraph EVENT(s) drawn: a caster-centred Pair circle must not exist (BIBLE s3, s9: the marked player's ring is the telegraph)") }
    if ($drawnAfterB -gt 0) { $failures.Add("run B: $drawnAfterB telegraph EVENT(s) drawn after the Warden's death at $deathTickB (R-PILE-10)") }
    if (-not $deathStepB) { $failures.Add("run B: no 'TICK $deathTickB' line for the Warden's death tick (check 8 has no step to read the cancels from)") }
    if ($cancelAtB -ne $liveB) { $failures.Add("run B: $cancelAtB telegraph_cancel EVENT(s) on the Warden's death tick $deathTickB, but $liveB telegraph(s) were live (TELEGRAPH-CANCEL-ON-DEATH)") }
    for ($i = 0; $i -lt $b.Lines.Count; $i++) {
        $l = $b.Lines[$i]
        if ($l -match '^CAST start ' -and $l -notmatch ' target=(\d+|none)$') { $failures.Add("run B: a CAST start line names no target: '$l'") }
        elseif ($i -gt $iDeathB -and $i -lt $iDeadEndB -and $l -match '^CAST start .* target=1$') { $failures.Add("run B: a cast starts on the dead Warden (died at $deathTickB, respawned at $respawnTickB): '$l'") }
        elseif ($i -gt $iDeathB -and $i -lt $iDeadEndB -and $l -match '^HUD ping ') { $failures.Add("run B: a HUD ping while the Warden is dead (died at $deathTickB, respawned at $respawnTickB): '$l'") }
    }
}
# R-BACKFILL-2 (BIBLE s9.1, f-47a7a6db; the #446 lane brief's R-WIPE-RESET: a wipe is a release and a run-back, not a run
# end): the solo wipe in the Pair does not end run B, so its 900 ticks print no run-end panel (headless, the run ends
# only at a depleted clear at 8402). Was lane 3C-d's wipe title "Party wiped on pull 2 of 4 -- key stays at +2", which a
# layout run can no longer print; the Pair's 2 of 4 numbering is still checked on the objective line above.
$runEndB = $respawnB
$wipeEndB = First $b.Lines '^HUD runend '
if ($wipeEndB) { $failures.Add("run B: a run-end panel line after the solo wipe, which releases and does not end the run (R-BACKFILL-2): '$wipeEndB'") }

# Run-end titles (lane 3C-d) on run B's lines.
$titlesB = Assert-RunEndTitles $b.Lines

Write-Output '--- asserted HUD lines ---'
foreach ($l in @($objB, $ping, $deathB, $check8B, $runEndB)) {
    if ($l) { Write-Output $l }
}
Write-RunEndTitles $titlesB

Complete-Scene "pingTicks=$PingTicks"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
