<#
.SYNOPSIS
  Headless scene test for the 3C HUD (ENGINE-PLAN phase 3, lane 3C; BIBLE s7 choice of three, s8 Slack, s10 pings):
  party frames, objective line, Slack bar, SPREAD/STACK labels and the run-end panel on the real run root.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 twice and reads the HUD's own
  "HUD ..." lines from the engine output (the HUD prints what it draws when --autorun is on):

    Run A (pull 1, the Choir, solo Warden):  -- --autorun --ticks 400 --solo
      1. exit 0 and "AUTORUN done ticks=400";
      2. "HUD party seat=1 name=Warden hp=H/400 res=R/M" (the party frame reads the human's health and resource);
      3. "HUD party seat=1 ... DOWNED" after the sim's death event for actor 1 (the DOWNED chip);
      4. "HUD objective pull=1/4 id=choir name="The Choir"" (the current pull's content name);
      5. "HUD slack ticks=620 of=620" first (Timer.InitialSlackTicks(2) = 600 + 20), then a lower value after the
         death (the sim spends 300 on a death and 500 on the wipe);
      6. "HUD runend result=depleted choices=3 cleared=0 ... title="Party wiped -- key depleted"" (the sim reports a
         lasting wipe as "depleted"; the panel must not call it cleared), three "HUD runend choice n=<1..3>
         slot=<slot> tier=<t> facet=..." lines (Rewards.RollRewardChoices), and "HUD runend cause seat=1" lines
         naming only seat 1's own causes.
    Run B (from pull 2, the Pair, solo Warden):  -- --autorun --ticks 400 --pull 2 --solo
      7. "HUD objective pull=2/4 id=pair name="The Pair"";
      8. at least one "HUD ping label=SPREAD" or "HUD ping label=STACK" line (from the Pair's mark telegraphs and
         mark effects in the sim state). The solo Warden is down before the Pair's first mark (about tick 294), so
         this line comes from enemy scripts the sim keeps stepping after run_end; run C proves a live-run call.
    Run C (pull 1, the three-seat bot party, lane 3H), twice with the same seed:  -- --autorun --ticks 3700
      9. "HUD party seat=1 name=Warden", "seat=2 name=Chanter", "seat=3 name=Cinder" (BotCommandSource drives 2, 3);
     10. in pull 1 (before the Pair spawns): a Chanter heal on a party seat and a Cinder damage hit on an enemy (at
         seed 1 the 3B healer role spends every GCD on heals and never casts Dissonance; the Cinder kit has no heal);
     11. the party reaches the Pair or run_end, is not wiped by tick 295, and a "HUD ping label=SPREAD|STACK" line
         appears before the party's third death (a call during a live run);
     12. a bot seat takes damage and every "HUD runend cause" line is seat 1's (the private run log's seat filter
         with more than one seat in the sim);
     13. "BOTS done commands=N hash=H" with N > 0, equal across the two runs (bot determinism).
    Run D (lane 3I, the armed boss; from pull 4, the boss pull, three-seat party, key 10 seed 7):
        -- --autorun --ticks 600 --pull 4 --key 10 --seed 7
     14. "BOSS armed twins=2 lockAt=50" before the first TICK line (HostRunSetup builds the BossConfig from
         content/data/enemies/hollow-warden.json and SimDriver arms every run); runs A and C print it too (the --solo
         path and the pull-1 party path arm the same way);
     15. "PULL spawn index=0 id=boss actors=900:hollowWarden.boss";
     16. two "boss_add_spawn" EVENTs, 901 hollowWarden.twinA and 902 hollowWarden.twinB, both for boss 900 (the Sundered
         Twins exist only when the encounter is armed: an unarmed run never emits it).
         Why --pull 4: no key reaches the boss from pull 1 under autorun. Lane 3I swept keys 2, 5, 8, 10, 12, 15, 20
         at seeds 1 and 7 for 8000 ticks: every run wiped in pull 2 (the Pair, run_end depleted at tick 3611-5381),
         because the human seat is PlayerInput's ScriptedWarden, not the 3B tank bot that 3F's k10 s7 probe used.
    Run E (lane 3I, the queued run; solo Warden, as ScreenSceneTest):  -- --autorun --ticks 480 --solo --requeue
        (--requeue: an autorun starts the queued run only when asked, so runs A-D and the other scene tests stay one
        run each; an interactive game always starts it)
     17. the first "RUN start" says "key=2 clause=none" (no queue yet: today's run);
     18. "SCREEN queue dungeon=hollow-vault key=3 clause=<id>" (the screen's autorun script queues key 3 + Clause 2);
     19. after it, a second "RUN start ... key=3 clause=<that id>" (the key is the run content's KeyLevel and the
         Clause is the sim's EncounterRules.ActiveClauseId, so a request that is dropped on the way reads key=2 or
         clause=none), followed by a second "BOSS armed" and "PULL spawn index=0 id=choir".
  Before 3I the arena never arms the boss and never reads the queue: 14-19 fail (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/HudSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 400,
    [int]$PingTicks = 400,
    [int]$PartyTicks = 3700,
    [int]$BossTicks = 600,
    [int]$QueueTicks = 480
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

function Invoke-Arena([int]$n, [string[]]$extra) {
    # --quit-after is a hang guard only: a correct run quits by itself at frame 3N.
    $guard = (3 * $n) + 240
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $guard '--' --autorun --ticks $n @extra 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') {
        Write-Output $text
        Write-Output 'HudSceneTest: CANNOT RUN (Godot binary not found)'
        exit 2
    }
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
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

# ---- Run A: pull 1 ---------------------------------------------------------------------------------------------
$a = Invoke-Arena $Ticks @('--solo')
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
if ($downed) {
    $iDowned = [array]::IndexOf($aRun1, $downed)
    $aliveAfter = $aRun1[($iDowned + 1)..($aRun1.Count - 1)] | Where-Object { $_ -match '^HUD party seat=1 ' -and $_ -notmatch '\bDOWNED\b' } | Select-Object -First 1
    if ($aliveAfter) { $failures.Add("run A: a party frame after DOWNED is not DOWNED: '$aliveAfter'") }
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
    $slackLower = $slackLines | Where-Object { $_ -match '^HUD slack ticks=(\d+) ' -and [int]$Matches[1] -lt 620 } | Select-Object -First 1
    if (-not $slackLower) { $failures.Add('run A: Slack never dropped below 620 after the death') }
}

$runEnd = First $a.Lines '^HUD runend result=depleted choices=3 cleared=0 tick=\d+ title="Party wiped -- key depleted"'
if (-not $runEnd) { $failures.Add('run A: no "HUD runend result=depleted choices=3 cleared=0 ... title=""Party wiped -- key depleted""" line') }
$choices = @($a.Lines | Where-Object { $_ -match '^HUD runend choice n=[123] slot=(weapon|armor|charm|ring|keystone) tier=\d+ facet="[^"]+"' })
if ($choices.Count -ne 3) { $failures.Add("run A: expected 3 'HUD runend choice' lines, got $($choices.Count)") }
$causes = @($a.Lines | Where-Object { $_ -match '^HUD runend cause ' })
if ($causes.Count -eq 0) {
    $failures.Add('run A: no "HUD runend cause" line (the private run log names what got the Warden)')
}
$foreign = @($causes | Where-Object { $_ -notmatch '^HUD runend cause seat=1 ' })
if ($foreign.Count -gt 0) { $failures.Add("run A: the run-end panel shows another seat's cause: '$($foreign[0])'") }

# ---- Run B: from pull 2 -----------------------------------------------------------------------------------------
$b = Invoke-Arena $PingTicks @('--pull', '2', '--solo')
if ($b.Code -ne 0) { $failures.Add("run B: godot exited $($b.Code) (expected 0)") }
$objB = First $b.Lines '^HUD objective pull=2/4 id=pair name="The Pair"'
if (-not $objB) { $failures.Add('run B: no "HUD objective pull=2/4 id=pair name=""The Pair""" line') }
$ping = First $b.Lines '^HUD ping label=(SPREAD|STACK) '
if (-not $ping) { $failures.Add('run B: no "HUD ping label=SPREAD|STACK" line') }

# ---- Run C: the three-seat bot party (lane 3H) -------------------------------------------------------------------
# The human Warden (seat 1) plus a Chanter (healer) and a Cinder (damage) driven by Keyrun.Bots through
# BotCommandSource. Run twice with the same seed: the bot command streams must hash equal (determinism).
$c = Invoke-Arena $PartyTicks @()
$c2 = Invoke-Arena $PartyTicks @()
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
if (-not $livePing) { $failures.Add('run C: no "HUD ping label=SPREAD|STACK" line while the run was live') }
# Private run log, two seats: the human's (seat 1) run-end panel never lists a bot seat's causes.
$causesC = @($c.Lines | Where-Object { $_ -match '^HUD runend cause ' })
$foreignC = @($causesC | Where-Object { $_ -notmatch '^HUD runend cause seat=1 ' })
if ($foreignC.Count -gt 0) { $failures.Add("run C: seat 1's run-end panel shows another seat's cause: '$($foreignC[0])'") }
$botHit = First $c.Lines '^EVENT \{.*"healthDamage":[1-9]\d*,.*"sourceId":(1\d\d|900),"targetId":[23],.*"type":"damage"'
if (-not $botHit) { $failures.Add('run C: no enemy damaged a bot seat, so the private-log seat filter is not exercised') }
$botsC = First $c.Lines '^BOTS done commands=\d+ hash=[0-9a-f]{16}$'
$botsC2 = First $c2.Lines '^BOTS done commands=\d+ hash=[0-9a-f]{16}$'
if (-not $botsC) { $failures.Add('run C: no "BOTS done commands=N hash=H" line') }
elseif ($botsC -match 'commands=0 ') { $failures.Add("run C: the bots issued no commands: '$botsC'") }
elseif ($botsC -ne $botsC2) { $failures.Add("run C: bot command stream differs across two same-seed runs: '$botsC' vs '$botsC2'") }
$armedC = Assert-BossArmed 'C' @(FirstRun $c.Lines)

# ---- Run D: the armed boss (lane 3I) -----------------------------------------------------------------------------
# From pull 4 (the boss pull): no key reaches the boss from pull 1 under autorun (see the header for the sweep).
$d = Invoke-Arena $BossTicks @('--pull', '4', '--key', '10', '--seed', '7')
if ($d.Code -ne 0) { $failures.Add("run D: godot exited $($d.Code) (expected 0)") }
$summaryD = First $d.Lines "^AUTORUN done ticks=$BossTicks "
if (-not $summaryD) { $failures.Add("run D: no 'AUTORUN done ticks=$BossTicks' summary") }
$armedD = Assert-BossArmed 'D' @(FirstRun $d.Lines)
$bossPull = First $d.Lines '^PULL spawn index=0 id=boss actors=900:hollowWarden\.boss$'
if (-not $bossPull) { $failures.Add('run D: no "PULL spawn index=0 id=boss actors=900:hollowWarden.boss" line') }
# Canonical EVENT JSON sorts keys: actorId, bossActorId, classId, tick, type.
$twins = @()
foreach ($t in @(@{ Id = 901; Class = 'hollowWarden.twinA' }, @{ Id = 902; Class = 'hollowWarden.twinB' })) {
    $ev = First $d.Lines "^EVENT \{`"actorId`":$($t.Id),`"bossActorId`":900,`"classId`":`"$([regex]::Escape($t.Class))`",.*`"type`":`"boss_add_spawn`""
    if (-not $ev) { $failures.Add("run D: no boss_add_spawn EVENT for $($t.Id) $($t.Class)") } else { $twins += $ev }
}
$bossRule = First $d.Lines '^EVENT \{.*"type":"(lock_open|lock_broken|lock_failed|boss_enrage)"'

# ---- Run E: the queued run (lane 3I) -----------------------------------------------------------------------------
# The solo Warden wipes in pull 1; the screen's autorun script takes reward 1, steps the key to 3, picks Clause 2 and
# queues. SimDriver must start the next run at that key with that Clause.
$e = Invoke-Arena $QueueTicks @('--solo', '--requeue')
if ($e.Code -ne 0) { $failures.Add("run E: godot exited $($e.Code) (expected 0)") }
$startsE = @($e.Lines | Where-Object { $_ -match '^RUN start ' })
$queueE = First $e.Lines '^SCREEN queue dungeon=hollow-vault key=3 clause=(echo|tide|hush|pact)$'
$requeued = $null
$armedE2 = $null
$pullE2 = $null
if ($startsE.Count -lt 1 -or $startsE[0] -notmatch ' dungeon=hollow-vault key=2 clause=none ') {
    $failures.Add("run E: the first run does not start at key 2 with no Clause: '$($startsE | Select-Object -First 1)'")
}
if (-not $queueE) {
    $failures.Add('run E: no "SCREEN queue dungeon=hollow-vault key=3 clause=<id>" line (the screen did not queue)')
} else {
    $null = $queueE -match 'clause=(\w+)$'
    $clauseE = $Matches[1]
    $iQueueE = [array]::IndexOf($e.Lines, $queueE)
    $after = @($e.Lines[($iQueueE + 1)..($e.Lines.Count - 1)])
    $requeued = First $after '^RUN start '
    if (-not $requeued) {
        $failures.Add('run E: no "RUN start" after the queue (the queued run never started)')
    } elseif ($requeued -notmatch " dungeon=hollow-vault key=3 clause=$clauseE ") {
        $failures.Add("run E: the queued run did not start at key 3 with Clause ${clauseE}: '$requeued'")
    } else {
        $iRe = [array]::IndexOf($after, $requeued)
        $rest = @($after[$iRe..($after.Count - 1)])
        $armedE2 = First $rest '^BOSS armed twins=2 lockAt=50\b'
        $pullE2 = First $rest '^PULL spawn index=0 id=choir '
        if (-not $armedE2) { $failures.Add('run E: the queued run is not armed (no second "BOSS armed" line)') }
        if (-not $pullE2) { $failures.Add('run E: the queued run did not spawn pull 1 (no second "PULL spawn index=0 id=choir")') }
    }
}

Write-Output '--- asserted HUD lines ---'
foreach ($l in @($summary, $party, $death, $downed, $objective, $slackFirst, $slackLower, $runEnd) + $choices + $causes + @($objB, $ping)) {
    if ($l) { Write-Output $l }
}
Write-Output '--- run C (bot party) ---'
foreach ($l in $seats + $botCasts + @($pull2, $tick295, $livePing, $runEndEv, $botHit) + $causesC + @($botsC, $botsC2)) {
    if ($l) { Write-Output $l }
}
Write-Output '--- run D (armed boss) and run E (queued run), lane 3I ---'
foreach ($l in @($armedA, $armedC, $summaryD, $armedD, $bossPull) + $twins + @($bossRule) + $startsE + @($queueE, $requeued, $armedE2, $pullE2)) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "HudSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "HudSceneTest: PASS ticks=$Ticks pingTicks=$PingTicks partyTicks=$PartyTicks bossTicks=$BossTicks queueTicks=$QueueTicks"
exit 0
