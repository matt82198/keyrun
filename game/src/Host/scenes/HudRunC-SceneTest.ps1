<#
.SYNOPSIS
  HUD scene run C (checks 9-13, 13a-13d, HOLDER-LOCK, COMBO-BLAME on run C): pull 1, the three-seat bot party (lane 3H),
  twice with the same seed.
  -- --autorun --ticks <PartyTicks> --careless-seat <CarelessSeat>

.DESCRIPTION
  Split from the former game/src/Host/HudSceneTest.ps1 (module-boundary review slice S3). The check text, the tick
  budget's history and the RED notes are in README.md beside this file. Run from the repo root:
    powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunC-SceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$PartyTicks = 9000,  # R-DOOR-1: the door at 100% makes the dungeon longer by design (was 7000)
    [string]$CarelessSeat = '3@300:pair'
)
$SceneName = 'HudRunC-SceneTest'
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

# ---- Run C: the three-seat bot party (lane 3H) -------------------------------------------------------------------
# The human Warden (seat 1) plus a Chanter (healer) and a Cinder (damage) driven by Keyrun.Bots through
# BotCommandSource. Run twice with the same seed: the bot command streams must hash equal (determinism).
# CARELESS-SEAT (STATE.md D43): one bot seat plays carelessly and pulls the Pair itself (header, run C).
$c = Invoke-Arena $PartyTicks @('--careless-seat', $CarelessSeat)
Write-SceneLog 'C' $c.Lines
$c2 = Invoke-Arena $PartyTicks @('--careless-seat', $CarelessSeat)
Write-SceneLog 'C repeat' $c2.Lines
if ($c.Code -ne 0) { $failures.Add("run C: godot exited $($c.Code) (expected 0)") }
$seats = @()
foreach ($s in @(@{ Seat = 1; Name = 'Warden' }, @{ Seat = 2; Name = 'Chanter' }, @{ Seat = 3; Name = 'Cinder' })) {
    $line = First $c.Lines "^HUD party seat=$($s.Seat) name=$($s.Name) hp=\d+/\d+ "
    if (-not $line) { $failures.Add("run C: no 'HUD party seat=$($s.Seat) name=$($s.Name)' line") } else { $seats += $line }
}
$iPull2 = -1
$pull2 = First $c.Lines '^PULL spawn index=1 id=pair '
if ($pull2) { $iPull2 = [array]::IndexOf($c.Lines, $pull2) }
$pull1Lines = if ($iPull2 -ge 0) { $c.Lines[0..$iPull2] } else { $c.Lines }
$botCasts = @()
# Keys are sorted in canonical EVENT JSON: amount, sourceId, targetId, tick, type. Party seats are 1-3, enemies 100+.
foreach ($want in @(
        @{ Who = 'Chanter heal on a party seat'; Re = '^EVENT \{"amount":[1-9]\d*,"sourceId":2,"targetId":[123],.*"type":"heal"' },
        @{ Who = 'Cinder damage on an enemy'; Re = '^EVENT \{.*"healthDamage":[1-9]\d*,.*"sourceId":3,"targetId":(1\d\d|900),.*"type":"damage"' })) {
    $ev = First $pull1Lines $want.Re
    if (-not $ev) { $failures.Add("run C: no $($want.Who) EVENT in pull 1") } else { $botCasts += $ev }
}
$runEndEv = First $c.Lines '^EVENT \{.*"type":"run_end"'
$iRunEnd = if ($runEndEv) { [array]::IndexOf($c.Lines, $runEndEv) } else { $c.Lines.Count }
if (-not $runEndEv -and -not $pull2) { $failures.Add('run C: the party neither reached the Pair (pull 2) nor run_end') }
# The party is live until its third seat dies (the sim keeps stepping enemy scripts after a wipe, so run_end alone
# is too late a bound: a solo Warden down at tick 273 still prints TICK 295 before run_end at 374).
$iWipe = $iRunEnd
$partyDeaths = 0
for ($i = 0; $i -lt $iRunEnd; $i++) {
    if ($c.Lines[$i] -match '^EVENT \{"actorId":[123],.*"type":"death"') {
        $partyDeaths++
        if ($partyDeaths -eq 3) { $iWipe = $i; break }
    }
}
$tick295 = First $c.Lines '^TICK 295 '
if (-not $tick295 -or [array]::IndexOf($c.Lines, $tick295) -gt $iWipe) { $failures.Add('run C: the party wiped before tick 295 (it did not survive to the Pair''s marks)') }
$livePing = $null
for ($i = 0; $i -lt $iWipe; $i++) {
    if ($c.Lines[$i] -match '^HUD ping label=(SPREAD|STACK) ') { $livePing = $c.Lines[$i]; break }
}
# R-WARDEN-FILLER-1 (keyrun #540, f-95d23cfc): Strike is the autorun Warden's filler, so Sundered stays up on the Pair
# and the Cinder's Shatter (Sundered + Cinder Payoff, a 1 s stun) keeps landing. Measured headless
# (RunCCarelessPairTests, seed 1 key 2): four Paired Warriors die before their first Spread Mark at engage+400, and the
# Shatter at 2450 interrupts the last one's pair.spreadMark, so no mark lands in run C (main: first call 2206). A Pair
# mark cast reached and stopped by the party is the same mechanic seen from the other side, so check 11 accepts either
# the live call or that interrupt before the wipe. The live call stays required in the default bot run (E2eAssert.ps1
# check 6; headless: mark_applied pair.spreadMark at 2333).
$markStopped = $null
if (-not $livePing) {
    for ($i = 0; $i -lt $iWipe; $i++) {
        if ($c.Lines[$i] -match '^EVENT \{"abilityId":"pair\.(spreadMark|stackCall)",.*"type":"interrupt"') { $markStopped = $c.Lines[$i]; break }
    }
}
if (-not $livePing -and -not $markStopped) { $failures.Add('run C: no "HUD ping label=SPREAD|STACK" line while the run was live, and no Pair mark cast was interrupted before the wipe') }
Write-Host "PING run C: live='$livePing' markStopped='$markStopped'"
# Private run log, two seats: the human's (seat 1) run-end panel never lists a bot seat's causes.
$causesC = @($c.Lines | Where-Object { $_ -match '^HUD runend cause ' })
$foreignC = @($causesC | Where-Object { $_ -notmatch '^HUD runend cause seat=1 ' })
if ($foreignC.Count -gt 0) { $failures.Add("run C: seat 1's run-end panel shows another seat's cause: '$($foreignC[0])'") }
# Lane COMBO-BLAME (BIBLE s4) on run C's lines (HudSceneCommon.ps1; run A checks its own).
Assert-ComboBlame 'C' $c.Lines
# Lane TUNE-BOSS (STATE.md D14): from pull 1 the party clears the dungeon at seed 1 key 2 (run_end timed at tick
# 6787): it reaches the boss, the Lock opens before run_end, and the run-end panel carries the clear title.
$runEndC = First $c.Lines '^HUD runend result='
if (-not $runEndC) {
    $failures.Add("run C: no 'HUD runend result=' line in $PartyTicks ticks (the party's run did not end on the run-end panel)")
} elseif ($runEndC -notmatch '^HUD runend result=timed choices=3 cleared=1 tick=\d+ title="Run cleared -- within par"$') {
    $failures.Add("run C: the party's run-end title is not the clear title 'Run cleared -- within par': '$runEndC'")
}
$bossC = First $c.Lines '^PULL spawn index=3 id=boss actors=900:hollowWarden\.boss$'
if (-not $bossC -or [array]::IndexOf($c.Lines, $bossC) -gt $iRunEnd) {
    $failures.Add("run C: the party never reached the boss before run_end (no 'PULL spawn index=3 id=boss actors=900:hollowWarden.boss' line)")
}
$lockC = First $c.Lines '^EVENT \{"actorId":900,"endsTick":\d+,"lockIndex":1,"tick":\d+,"type":"lock_open"\}$'
if (-not $lockC -or [array]::IndexOf($c.Lines, $lockC) -gt $iRunEnd) {
    $failures.Add("run C: the Lock never opened before run_end (no 'EVENT {""actorId"":900,...,""lockIndex"":1,...,""type"":""lock_open""}' line)")
}
# HOLDER-LOCK (STATE.md D30): every engaged enemy stays on its holder, and the autorun Warden (seat 1) engages every
# pull, so in run C no enemy damages a bot seat any more (measured on dcfb978: all six cause rows are seat 1). The old
# "an enemy damaged a bot seat" requirement is gone; the run instead must show enemy damage landing on the holder,
# seat 1. The private-log seat filter above is no longer exercised by run C (no foreign cause exists to filter).
$enemyDmg = '^EVENT \{.*"healthDamage":[1-9]\d*,.*"sourceId":(1\d\d|90\d),"targetId":(\d+),.*"type":"damage"'
$holderHits = @($c.Lines | Where-Object { $_ -match $enemyDmg -and $Matches[2] -eq '1' }).Count
$botHits = @($c.Lines | Where-Object { $_ -match $enemyDmg -and ($Matches[2] -eq '2' -or $Matches[2] -eq '3') }).Count
Write-Host "HOLDER-LOCK run C: enemy damage on seat 1=$holderHits, on bot seats=$botHits"
if ($holderHits -eq 0) { $failures.Add('run C: no enemy damaged seat 1, the Warden that engages and holds every pull (HOLDER-LOCK D30)') }
$botsC = First $c.Lines '^BOTS done commands=\d+ hash=[0-9a-f]{16}$'
$botsC2 = First $c2.Lines '^BOTS done commands=\d+ hash=[0-9a-f]{16}$'
if (-not $botsC) { $failures.Add('run C: no "BOTS done commands=N hash=H" line') }
elseif ($botsC -match 'commands=0 ') { $failures.Add("run C: the bots issued no commands: '$botsC'") }
elseif ($botsC -ne $botsC2) { $failures.Add("run C: bot command stream differs across two same-seed runs: '$botsC' vs '$botsC2'") }
$armedC = Assert-BossArmed 'C' @(FirstRun $c.Lines)
# Lane NAV-CORE 13a (STATE.md D18): the Warden walks the route room by room, and the pulls engage in route order.
# R-PINS-1 (f-f0c886eb, aggro-chase): the old "each pull spawns only once the Warden is in its room" predates D44's
# prespawned packs and held only while a pull needed a seat inside its 4 m ring. Pulls now aggro on each member's own
# 8 m radius, and run C's careless seat pulls the Pair from corridor-2 by design (RunCCarelessPairTests proves it stood in
# the Pair's reach). The property kept: pull r engages after pull r-1 and only once the Warden has reached the room
# before it on the route (room r-1), never ahead of the party's progress.
$cRun = @(FirstRun $c.Lines)
$roomOrder = @('choir-room', 'pair-hall', 'seed-chamber', 'warden-arena')
$roomsC = @()
$iPrevRoom = -1
$iPrevSpawn = -1
for ($r = 0; $r -lt $roomOrder.Count; $r++) {
    $enter = First $cRun "^EVENT \{`"actorId`":1,`"room`":`"$($roomOrder[$r])`",`"tick`":\d+,`"type`":`"room_enter`"\}$"
    $spawn = First $cRun "^PULL spawn index=$r "
    if (-not $enter) { $failures.Add("run C: the Warden never entered $($roomOrder[$r]) (no room_enter EVENT for actor 1)"); continue }
    $iEnter = [array]::IndexOf($cRun, $enter)
    if ($iEnter -le $iPrevRoom) { $failures.Add("run C: the Warden entered $($roomOrder[$r]) before the room ahead of it on the route") }
    $iRoomBefore = $iPrevRoom
    $iPrevRoom = $iEnter
    if (-not $spawn) { $failures.Add("run C: no 'PULL spawn index=$r' line (the pull of $($roomOrder[$r]) never spawned)") }
    else {
        $iSpawn = [array]::IndexOf($cRun, $spawn)
        if ($iSpawn -lt $iPrevSpawn) { $failures.Add("run C: pull $r spawned before pull $($r - 1): '$spawn'") }
        if ($r -gt 0 -and $iSpawn -lt $iRoomBefore) { $failures.Add("run C: pull $r spawned before the Warden entered $($roomOrder[$r - 1]), the room before it: '$spawn'") }
        $iPrevSpawn = $iSpawn
    }
    $roomsC += $enter
}
$startSpawn = First $cRun '^PULL spawn index=0 '
$firstTick = First $cRun '^TICK '
if ($startSpawn -and $firstTick -and [array]::IndexOf($cRun, $startSpawn) -lt [array]::IndexOf($cRun, $firstTick)) {
    $failures.Add('run C: pull 1 spawned at run start, on top of the party (it must wait for the Choir room)')
}
# 13b: the Sundered Twins spawn in the full run (STATE.md NEXT ORDER step 3), before run_end.
foreach ($t in @(@{ Id = 901; Class = 'hollowWarden.twinA' }, @{ Id = 902; Class = 'hollowWarden.twinB' })) {
    $ev = First $c.Lines "^EVENT \{`"actorId`":$($t.Id),`"bossActorId`":900,`"classId`":`"$([regex]::Escape($t.Class))`",.*`"type`":`"boss_add_spawn`""
    if (-not $ev -or [array]::IndexOf($c.Lines, $ev) -gt $iRunEnd) { $failures.Add("run C: no boss_add_spawn EVENT for $($t.Id) $($t.Class) before run_end") }
}
# Lane WARDEN-ENGAGES (KF2b): the autorun Warden lands damage on the Choir at least every 300 ticks until it is clear.
foreach ($f in @(& (Join-Path $PSScriptRoot '../WardenEngagesTest.ps1') -Lines $cRun -PullId 'choir' -MaxGap 300)) { $failures.Add("run C: $f") }
# 13c. Lane HUD-DECLUTTER: friendly casts in the party frames, only the human's damage numbers, enemy health in unit
# frames, enemy cast bars kept (src/Hud/HudDeclutterTest.ps1 holds the four checks and their RED on main).
foreach ($f in @(& (Join-Path $PSScriptRoot '../../Hud/HudDeclutterTest.ps1') -Lines $cRun)) { $failures.Add("run C: $f") }
# 13d. Lane fix/target-frame-after-death (P0): a cleared run's objective counts the enemies still alive at run_end (a
# Sundered Twin that outlives the boss), never "No enemies remain" over a live target frame (src/Hud/ObjectiveClearTest.ps1).
foreach ($f in @(& (Join-Path $PSScriptRoot '../../Hud/ObjectiveClearTest.ps1') -Lines $cRun)) { $failures.Add("run C: $f") }

# Run-end titles (lane 3C-d) on run C's lines and its same-seed repeat's.
$titlesC = Assert-RunEndTitles (@($c.Lines) + @($c2.Lines))

Write-Output '--- run C (bot party) ---'
foreach ($l in $seats + $botCasts + @($pull2, $tick295, $livePing, $bossC, $lockC, $runEndEv, $runEndC, $botHit) + $causesC + @($botsC, $botsC2, $armedC)) {
    if ($l) { Write-Output $l }
}
Write-RunEndTitles $titlesC

Complete-Scene "partyTicks=$PartyTicks carelessSeat=$CarelessSeat"
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
