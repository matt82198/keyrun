<#
.SYNOPSIS
  HUD scene run A (checks 1-6 and 40, COMBO-BLAME on run A): pull 1, the Choir, solo Warden.
  -- --autorun --ticks <Ticks> --solo

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text, the tick
  budget's history and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunA-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 10000
)
$SceneName = 'HudRunA-SceneTest'
. (Join-Path $PSScriptRoot 'HudSceneCommon.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner "$SceneName.ps1" -ExitCodeOnBusy 2
try {
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'

function Invoke-Arena([int]$n, [string[]]$extra) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    # --view-size 1280x720 (Main.cs ApplyViewSize, as HudTooltip-SceneTest.ps1:73-75): a headless window is 64x64, too small for the timer box; render-only, the sim does not read it.
    $guard = (3 * $n) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $n --view-size 1280x720 @extra 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Assert-GodotFound $rc $text
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}

# ---- Run A: pull 1 ---------------------------------------------------------------------------------------------
$a = Invoke-Arena $Ticks @('--solo')
Write-SceneLog 'A' $a.Lines
if ($a.Code -ne 0) { $failures.Add("run A: godot exited $($a.Code) (expected 0)") }
$summary = First $a.Lines "^AUTORUN done ticks=$Ticks "
if (-not $summary) { $failures.Add("run A: no 'AUTORUN done ticks=$Ticks' summary") }

$party = First $a.Lines '^HUD party seat=1 name=Warden hp=\d+/400 res=\d+/\d+'
if (-not $party) { $failures.Add('run A: no "HUD party seat=1 name=Warden hp=H/400 res=R/M" line') }

$death = First $a.Lines '^EVENT \{"actorId":1,.*"type":"death"'
$downed = First $a.Lines '^HUD party seat=1 .*\bDOWNED\b'
if (-not $death) {
    $failures.Add('run A: the Warden did not die (no death EVENT for actor 1); DOWNED cannot be checked')
} elseif (-not $downed) {
    $failures.Add('run A: actor 1 died but no "HUD party seat=1 ... DOWNED" line')
} else {
    $iDeath = [array]::IndexOf($a.Lines, $death)
    $iDowned = [array]::IndexOf($a.Lines, $downed)
    if ($iDowned -lt $iDeath) { $failures.Add('run A: DOWNED was drawn before the sim death event') }
}
$aliveAfter = $null
# Up to the queued run's start only: a fresh run's Warden is alive again (lane 3I consumes the screen's queue).
$aRun1 = @(FirstRun $a.Lines)
# R-BACKFILL-2 (BIBLE s9.1, f-47a7a6db; the #446 lane brief's R-WIPE-RESET: a wipe is a release and a run-back, not a
# run end): the dead Warden respawns at the entrance RunWiring.ReleaseDelayTicks (100) after his death step, so the
# DOWNED chip holds from his death to his respawn EVENT, and the frame after it reads him alive at full health.
$respawnA = $null
if ($downed) {
    $iDowned = [array]::IndexOf($aRun1, $downed)
    $iRespawnA = $aRun1.Count
    for ($i = $iDowned + 1; $i -lt $aRun1.Count; $i++) {
        if ($aRun1[$i] -match '^EVENT \{"actorId":1,"tick":(\d+),"type":"respawn"\}$') { $respawnA = $aRun1[$i]; $iRespawnA = $i; break }
    }
    if ($iRespawnA -gt $iDowned + 1) {
        $aliveAfter = $aRun1[($iDowned + 1)..($iRespawnA - 1)] | Where-Object { $_ -match '^HUD party seat=1 ' -and $_ -notmatch '\bDOWNED\b' } | Select-Object -First 1
    }
    if ($aliveAfter) { $failures.Add("run A: a party frame after DOWNED and before the respawn is not DOWNED: '$aliveAfter'") }
    if (-not $respawnA) {
        $failures.Add('run A: no respawn EVENT for actor 1 after DOWNED (R-BACKFILL-2: the dead Warden releases and respawns)')
    } else {
        $null = $respawnA -match '"tick":(\d+)'
        $respawnTickA = [int]$Matches[1]
        $null = $death -match '"tick":(\d+)'
        $deathTickA = [int]$Matches[1]
        if ($respawnTickA -ne $deathTickA + 1 + 100) { $failures.Add("run A: the Warden respawned at $respawnTickA, not death $deathTickA + 1 + RunWiring.ReleaseDelayTicks 100: '$respawnA'") }
        $frameBack = $aRun1[($iRespawnA + 1)..($aRun1.Count - 1)] | Where-Object { $_ -match '^HUD party seat=1 ' } | Select-Object -First 1
        if (-not $frameBack -or $frameBack -match '\bDOWNED\b' -or $frameBack -notmatch ' hp=400/400 ') { $failures.Add("run A: the first party frame after the respawn does not read the Warden alive at full health: '$frameBack'") }
    }
}
$armedA = Assert-BossArmed 'A' $aRun1

$objective = First $a.Lines '^HUD objective pull=1/4 id=choir name="The Choir"'
if (-not $objective) { $failures.Add('run A: no "HUD objective pull=1/4 id=choir name=""The Choir""" line') }

$slackLines = @($a.Lines | Where-Object { $_ -match '^HUD slack ticks=\d+ of=\d+' })
$slackFirst = $null
$slackLower = $null
if ($slackLines.Count -eq 0) {
    $failures.Add('run A: no "HUD slack" line')
} else {
    $slackFirst = $slackLines[0]
    if ($slackFirst -notmatch '^HUD slack ticks=620 of=620\b') {
        $failures.Add("run A: first Slack line is not the key-2 bank 620 of 620: '$slackFirst'")
    }
    # D46 (Mythic+): the death adds time to the run clock (penalty=) and never drains the bank.
    $slackLower = $slackLines | Where-Object { $_ -match '\bpenalty=(\d+)\b' -and [int]$Matches[1] -gt 0 } | Select-Object -First 1
    if (-not $slackLower) { $failures.Add('run A: no "HUD slack ... penalty=N" line above 0 after the death (the death added no time to the run clock)') }
    $drained = $slackLines | Where-Object { $_ -match '^HUD slack ticks=(\d+) ' -and [int]$Matches[1] -lt 620 } | Select-Object -First 1
    if ($drained) { $failures.Add("run A: a death drained the Slack bank (D46: deaths only add time to the clock): '$drained'") }
}

# f-1bf2dace: the timer text ("+2  3:42 / par 12:00") has its own box right of the Slack fill bar (HudLayout.SlackSplit),
# so the fill edge and the strip's tier marks never cross it; the box holds the measured text with 6 px each side.
$timerLines = @($a.Lines | Where-Object { $_ -match '^HUD timer bar=\d+,\d+,\d+,\d+ box=\d+,\d+,\d+,\d+ text=\d+ viewport=[1-9]\d*x\d+ label=' })
if ($timerLines.Count -eq 0) {
    $failures.Add("run A: no 'HUD timer bar=X,Y,W,H box=X,Y,W,H text=W viewport=WxH label=...' line with a laid-out viewport (got '$(First $a.Lines '^HUD timer ')')")
}
foreach ($t in $timerLines) {
    $null = $t -match '^HUD timer bar=(\d+),(\d+),(\d+),(\d+) box=(\d+),(\d+),(\d+),(\d+) text=(\d+) viewport=\d+x\d+ label=(.*)$'
    $barRight = [int]$Matches[1] + [int]$Matches[3]
    $boxX = [int]$Matches[5]
    $boxW = [int]$Matches[7]
    $textW = [int]$Matches[9]
    $label = $Matches[10]
    if ($barRight + 4 -gt $boxX) { $failures.Add("run A: the Slack bar runs into the timer box (bar right $barRight, box x $boxX): '$t'") }
    if ($textW + 12 -gt $boxW) { $failures.Add("run A: the timer box ($boxW px) cannot hold its text ($textW px + 6 px each side): '$t'") }
    if ([int]$Matches[2] -ne [int]$Matches[6] -or [int]$Matches[4] -ne [int]$Matches[8]) { $failures.Add("run A: the timer box is not on the Slack bar's row: '$t'") }
    if ($label -notmatch '^\+2  \d+:\d\d / par \d+:\d\d$') { $failures.Add("run A: the timer label lost the key level, clock or par: '$t'") }
}

# R-BACKFILL-2 (the #446 lane brief's R-WIPE-RESET): the wipe released the Warden, so run A ends at the timer cap
# (Timer.CapTicks, tick 9600 headless), depleted and uncleared, and RunEndTitle.ForRun titles it "Out of time"; the
# wipe title "Party wiped on pull 1 of 4" belonged to the superseded rule that a lasting wipe ends the run.
$runEnd = First $a.Lines '^HUD runend result=depleted choices=3 cleared=0 tick=\d+ title="Out of time -- key stays at \+2"$'
if (-not $runEnd) { $failures.Add("run A: no 'HUD runend result=depleted choices=3 cleared=0 ... title=""Out of time -- key stays at +2""' line (the timer cap's end, R-BACKFILL-2; got '$(First $a.Lines '^HUD runend result=')')") }
# Key progression (BIBLE s8, Matt 2026-10-20): the untimed key-2 run holds its key, no depletion drop, and the panel says so.
$keyLine = First $a.Lines '^HUD runend key from=2 to=2 tier=0$'
if (-not $keyLine) { $failures.Add("run A: no 'HUD runend key from=2 to=2 tier=0' line (an untimed key holds its level; got '$(First $a.Lines '^HUD runend key ')')") }
$choices = @($a.Lines | Where-Object { $_ -match '^HUD runend choice n=[123] slot=(weapon|armor|charm|ring|keystone) tier=\d+ facet="[^"]+"' })
if ($choices.Count -ne 3) { $failures.Add("run A: expected 3 'HUD runend choice' lines, got $($choices.Count)") }
# Lane 3C-d: the cards roll over the Warden's content facet pool, so each names a real facet id and its kit ability.
foreach ($ch in $choices) {
    if ($ch -match ' ability=ability-[0-9]') { $failures.Add("run A: a run-end card still carries a placeholder ability id: '$ch'") }
    elseif ($ch -notmatch '^HUD runend choice n=[123] slot=(\w+) tier=(\d+) facet="[^"]+" ability=warden\.[A-Za-z]+ charges=\d+ id=reward-\1-warden\.\1\.[A-Za-z]+-t\2-\d$') {
        $failures.Add("run A: a run-end card does not name a Warden content facet (want ability=warden.<id> ... id=reward-<slot>-warden.<slot>.<facet>-t<t>-<i>): '$ch'")
    }
}
$causes = @($a.Lines | Where-Object { $_ -match '^HUD runend cause ' })
if ($causes.Count -eq 0) {
    $failures.Add('run A: no "HUD runend cause" line (the private run log names what got the Warden)')
}
$foreign = @($causes | Where-Object { $_ -notmatch '^HUD runend cause seat=1 ' })
if ($foreign.Count -gt 0) { $failures.Add("run A: the run-end panel shows another seat's cause: '$($foreign[0])'") }
# 40 (lane ABILITY-ICONS): every Warden bar slot resolved an icon and loaded a 64x64 texture.
$barIcons = @($aRun1 | Where-Object { $_ -match '^HUD bar slot=' })
foreach ($k in @('1', '2', '3', '4', '5', 'Q', 'E', 'R', 'F')) {
    $row = $barIcons | Where-Object { $_ -match "^HUD bar slot=$k " } | Select-Object -First 1
    if (-not $row) { $failures.Add("run A: no 'HUD bar slot=$k' line (the ability bar printed no icon for slot $k)") }
    elseif ($row -notmatch '^HUD bar slot=\w ability=warden\.[A-Za-z]+ icon=res://assets/kenney-board-game-icons/[a-z0-9_]+\.png tex=64x64$') {
        $failures.Add("run A: ability bar slot $k has no loaded icon: '$row'")
    }
}

# Lane COMBO-BLAME (BIBLE s4) on run A's lines (HudSceneCommon.ps1).
Assert-ComboBlame 'A' $a.Lines

# Run-end titles (lane 3C-d) on run A's lines; run A carries the "the title checks did not run" guard.
$titlesA = Assert-RunEndTitles $a.Lines -RequireTitle

Write-Output '--- asserted HUD lines ---'
foreach ($l in @($summary, $party, $death, $downed, $respawnA, $objective, $slackFirst, $slackLower, $runEnd) + $choices + $causes + @($armedA)) {
    if ($l) { Write-Output $l }
}
Write-RunEndTitles $titlesA

Complete-Scene "ticks=$Ticks"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
