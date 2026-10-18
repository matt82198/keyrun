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
         clause=none), followed by a second "BOSS armed" and "PULL spawn index=0 id=choir";
     20. then "QUEUE consume n=1 size=0" (lane 3J: the request that started the run leaves the Queue node, so it can
         never also refill a seat).
    Run F (lane 3J, backfill in the real loop; three-seat party, key 2, no --requeue):
        -- --autorun --ticks 3300 --abandon-seat <AbandonSeat> --joiner-key 2
        (--abandon-seat A@T: SimDriver calls AbandonSeat(A) at run tick T and, with --joiner-key K, adds one
        QueueRequest at key K straight to the Queue node at Hud/Screen/Queue; this is the scripted path, the screen's
        Queue button is not pressed mid-run)
     21. "SEAT abandon actor=A tick=T" in pull 1, then "QUEUE add dungeon=hollow-vault key=2";
     22. the party survives to "PULL spawn index=1 id=pair", and in that same step (no TICK line between them)
         "SEAT refill actor=A tick=X key=2" with X = that step's TICK tick + 1 (Backfill.Step's FillTick: the first
         tick whose commands are the joiner's), then "QUEUE consume n=1 size=0";
     23. the open seat stands idle: no cast_complete EVENT with "actorId":A between the abandon and the refill, the
         refilled seat casts again after it (the joiner's commands run from the pull start), and no party seat dies
         before pull 2. Default A@T = 1@1000 (the human seat leaves at tick 1000; seed 1 key 2 pull 2 starts at
         tick 3152 with all three seats alive; 2@2000 instead loses seats 1 and 2 in pull 1).
    Run G (lane 3J, key mismatch): as F with --joiner-key 3
     24. "SEAT abandon", "QUEUE add ... key=3" and "PULL spawn index=1" appear, but no "SEAT refill" line and no
         "QUEUE consume" line (Backfill.Admits is exact key equality: a key-3 joiner is refused a key-2 run).
  Before 3I the arena never arms the boss and never reads the queue: 14-19 fail (RED). Before 3J SimDriver steps
  RunRoot.StepRun and ignores --abandon-seat: 20-24 fail (RED).
    Source checks (lane HUD-FU; the ability bar draws with immediate-mode DrawString, so no node or line shows it):
     25. game/src/Hud/AbilityBar.cs draws no run-over banner: no "Run over" text and no RunEnd read (since 3C the
         run-end panel owns the run-over state), and RunEndPanel.cs still draws "Enter starts a fresh run";
     26. game/src/Hud/Screen.cs no longer says the host ignores the queue ("does not read the queued"): since 3I/3J
         the host starts the queued run (ScreenSceneTest asserts the caption it now draws).
  Before HUD-FU the bar still drew "Run over: <result> - press Enter to start again" and the Queue caption said the
  host does not read the queued key and Clause: 25-26 fail (RED).
    Run H (lane 3D-b, the screen first; the interactive launch, NO --autorun, three-seat party):  -- (no user args)
        (headless cannot press Queue, so --quit-after <ScreenFirstFrames> ends it; --autorun and --solo still start a
        run at once, which runs A-G prove)
     27. exit 0, "SCREEN first open=1 key=2" (SimDriver opened the between-runs screen before any run), and no
         "RUN start", "BOSS armed" or "PULL spawn" line in <ScreenFirstFrames> frames (the first run waits for the
         screen's Queue, Screen.Queued -> StartQueuedRun). Before 3D-b the launch starts key 2 at once: 27 fails (RED).
    Run I (lane 3D-c, a wipe goes back to the screen; interactive, three-seat party, from pull 2 so it wipes fast):
        -- --press-enter 3 --pull 2   (--quit-after <WipeFrames>)
        (--press-enter N: SimDriver's scripted player pushes Enter through the viewport each time the game waits on
        it, the screen up or the run ended, up to N presses; the real keyboard path, not a direct call)
     28. "SCREEN first open=1 key=2", press 1 queues the first run ("RUN start ... clause=<id>", not none), the party
         wipes ("run_end" EVENT, result depleted), press 2 on the run-end panel prints "SCREEN reopen
         result=depleted open=1 key=2" and NO "RUN start" comes between the run_end and that line, nor between it and
         press 3; press 3 is the screen's Queue, and the next "RUN start" carries a Clause (clause=<id>, never none:
         it came through Screen.Queued -> StartQueuedRun, ENGINE-PLAN "choose one, then Screen"). Before 3D-c Enter
         after a wipe called StartRun(null): a "RUN start ... clause=none" follows press 2 and 28 fails (RED).
    Run J (lane 3H-c, a human G-ping reaches the bot party; interactive, three-seat party, pull 1):
        -- --press-enter 1 --press-ping <PingAt>   (--quit-after <PingFrames>)
        (--press-ping T: once the run reaches tick T, SimDriver's scripted player pushes one G through the viewport, the
        real keyboard path into PlayerInput; press 1 is the screen's Queue, which starts the run)
     29. "INPUT ping tick=T", then "PING routed kind=spot target=none tick=N" with N >= T (the idle human tank has no
         target, so G is a spot ping at the Warden: SimDriver turned PlayerInput.Pinged into a bots PingEvent), then a
         "PING react actor=A kind=move tick=M" with A a bot seat (2 Chanter, 3 Cinder) and N <= M <= N + 40
         (PingsPolicy.PingMoveTicks): the bot runner's batch carries that seat's ping reaction. Before 3H-c nothing
         subscribed to PlayerInput.Pinged: no "PING routed" line and 29 fails (RED).
    Run K (lane 3E-b, a recorded input log replays through the real input path; interactive, three-seat party):
      K1  -- --record-input <log> --press-enter 3 --press-ping 2 --pull 2 --log <k1>   (--quit-after <RecordFrames>)
      K2  -- --replay-input <log> --pull 2 --log <k2>                                  (--quit-after <RecordFrames>)
        (--record-input P: SimDriver appends every key press and release the game reads, stamped with the host's
        stepped-tick count, and writes P as keyrun.input-log/1 at quit; --replay-input P feeds each event back through
        Input.ParseInputEvent at its tick, the path PlayerInput polls and the viewport delivers)
     30. K1: exit 0, "INPUT record done events=N path=<log>" with N >= 2, and the file parses as keyrun.input-log/1
         (seed 1, key 2) with exactly N events whose ticks never go down; it equals the committed example
         game/fixtures/input/example-enter-ping.input-log.json byte for byte (the fixture is a real, current instance);
     31. K2: exit 0, N "INPUT replay tick=T key=K pressed=B" lines matching the log's events in order, then
         "INPUT replay done events=N last=T" (T = the last event's tick), and no scripted "INPUT enter"/"INPUT ping";
     32. the K2 run log carries the same "RUN start", "SCREEN reopen", "PING routed" and run_end lines as K1's, each at
         the same tick (a marker's tick is the last TICK line before it in the --log file; PING routed also carries its
         own): K1's markers are exactly the first markers of K2's log (K2 starts its first run a few frames sooner, so
         it may run further in the same frames). Before 3E-b SimDriver ignores both flags: no "INPUT record done" line and 30
         fails (RED).

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/HudSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 400,
    [int]$PingTicks = 400,
    [int]$PartyTicks = 3700,
    [int]$BossTicks = 600,
    [int]$QueueTicks = 480,
    [int]$BackfillTicks = 3300,
    [string]$AbandonSeat = '1@1000',
    [int]$ScreenFirstFrames = 240,
    [int]$WipeFrames = 2400,
    [int]$PingAt = 100,
    [int]$PingFrames = 900,
    [int]$RecordFrames = 2400
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

# ---- Source checks 25-26 (lane HUD-FU): no stale run-over banner, no stale queue caption ----------------------
$barSrc = Get-Content -Raw (Join-Path $repo 'game/src/Hud/AbilityBar.cs')
$panelSrc = Get-Content -Raw (Join-Path $repo 'game/src/Hud/RunEndPanel.cs')
$screenSrc = Get-Content -Raw (Join-Path $repo 'game/src/Hud/Screen.cs')
if ($barSrc -match 'Run over') { $failures.Add('AbilityBar.cs still draws a "Run over" banner (the run-end panel owns the run-over state)') }
if ($barSrc -match '\bRunEnd\b') { $failures.Add('AbilityBar.cs still reads run.RunEnd (only the stale run-over banner did)') }
if ($panelSrc -notmatch 'Enter starts a fresh run') { $failures.Add('RunEndPanel.cs no longer draws "Enter starts a fresh run" (the only Enter hint once the bar banner is gone)') }
if ($screenSrc -match 'does not read the queued') { $failures.Add('Screen.cs still says the host does not read the queued key and Clause (false since 3I/3J)') }

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
        $consumedE = First $rest '^QUEUE consume n=1 size=0$'
        if (-not $consumedE) { $failures.Add('run E: the request that started the queued run is still in the Queue node (no "QUEUE consume n=1 size=0" after it)') }
    }
}

# ---- Runs F and G: backfill in the real loop (lane 3J) -----------------------------------------------------------
# SimDriver steps Backfill.Step: the seat abandoned in pull 1 is refilled at pull 2's start by a matching-key joiner.
$seatParts = $AbandonSeat -split '@'
$seatActor = $seatParts[0]
$seatTick = $seatParts[1]
function Invoke-Backfill([string]$run, [int]$joinerKey) {
    $r = Invoke-Arena $BackfillTicks @('--abandon-seat', $AbandonSeat, '--joiner-key', "$joinerKey")
    if ($r.Code -ne 0) { $failures.Add("run ${run}: godot exited $($r.Code) (expected 0)") }
    $lines = @(FirstRun $r.Lines)
    $res = [pscustomobject]@{ Lines = $lines; Abandon = $null; Add = $null; Pull = $null; IPull = -1; Refill = $null; Consume = $null }
    $res.Abandon = First $lines "^SEAT abandon actor=$seatActor tick=$seatTick$"
    $res.Add = First $lines "^QUEUE add dungeon=hollow-vault key=$joinerKey "
    $res.Pull = First $lines '^PULL spawn index=1 id=pair '
    if ($res.Pull) { $res.IPull = [array]::IndexOf($lines, $res.Pull) }
    $res.Refill = First $lines '^SEAT refill '
    $res.Consume = First $lines '^QUEUE consume '
    if (-not $res.Abandon) { $failures.Add("run ${run}: no 'SEAT abandon actor=$seatActor tick=$seatTick' line") }
    if (-not $res.Add) { $failures.Add("run ${run}: no 'QUEUE add dungeon=hollow-vault key=$joinerKey' line (the scripted joiner never reached the Queue node)") }
    if (-not $res.Pull) {
        $failures.Add("run ${run}: the party did not reach 'PULL spawn index=1 id=pair' (no next pull start to refill at)")
    } elseif ($res.Abandon -and [array]::IndexOf($lines, $res.Abandon) -gt $res.IPull) {
        $failures.Add("run ${run}: the abandon came after pull 2 spawned (it must be in pull 1)")
    }
    return $res
}

$f = Invoke-Backfill 'F' 2
if ($f.Pull) {
    $refillF = $null
    for ($i = $f.IPull + 1; $i -lt $f.Lines.Count; $i++) {
        if ($f.Lines[$i] -match '^TICK ') { break }
        if ($f.Lines[$i] -match '^SEAT refill ') { $refillF = $f.Lines[$i]; break }
    }
    $stepTick = $null
    for ($i = $f.IPull - 1; $i -ge 0; $i--) {
        if ($f.Lines[$i] -match '^TICK (\d+) ') { $stepTick = [int]$Matches[1]; break }
    }
    $wantFill = if ($null -ne $stepTick) { $stepTick + 1 } else { -1 }
    if (-not $refillF) {
        $failures.Add("run F: no 'SEAT refill' line in the step that spawned pull 2 (first refill line: '$($f.Refill)')")
    } elseif ($refillF -notmatch "^SEAT refill actor=$seatActor tick=$wantFill key=2$") {
        $failures.Add("run F: wrong refill line: '$refillF' (want 'SEAT refill actor=$seatActor tick=$wantFill key=2')")
    } else {
        $iRefillF = [array]::IndexOf($f.Lines, $refillF)
        $consumeF = First @($f.Lines[$iRefillF..($f.Lines.Count - 1)]) '^QUEUE consume n=1 size=0$'
        if (-not $consumeF) { $failures.Add('run F: the joiner was not consumed from the Queue node (no "QUEUE consume n=1 size=0" after the refill)') }
        if ($f.Abandon) {
            $iAbandonF = [array]::IndexOf($f.Lines, $f.Abandon)
            $idle = @($f.Lines[$iAbandonF..$iRefillF] | Where-Object { $_ -match "^EVENT \{`"abilityId`":`"[^`"]+`",`"actorId`":$seatActor,.*`"type`":`"cast_complete`"" })
            if ($idle.Count -gt 0) { $failures.Add("run F: the open seat acted before the refill: '$($idle[0])'") }
        }
        $joinerActs = First @($f.Lines[$iRefillF..($f.Lines.Count - 1)]) "^EVENT \{`"abilityId`":`"[^`"]+`",`"actorId`":$seatActor,.*`"type`":`"cast_complete`""
        if (-not $joinerActs) { $failures.Add('run F: the refilled seat never acted after the refill (the joiner''s commands are dropped)') }
    }
    $deadEarly = @($f.Lines[0..$f.IPull] | Where-Object { $_ -match '^EVENT \{"actorId":[123],.*"type":"death"' })
    if ($deadEarly.Count -gt 0) { $failures.Add("run F: a party seat died before pull 2 (pick an abandon the party survives): '$($deadEarly[0])'") }
}

$g = Invoke-Backfill 'G' 3
if ($g.Refill) { $failures.Add("run G: a key-3 joiner refilled a key-2 run: '$($g.Refill)'") }
if ($g.Consume) { $failures.Add("run G: the key-3 joiner was consumed: '$($g.Consume)'") }

# ---- Run H: the interactive launch opens the screen first (lane 3D-b) ----------------------------------------------
$ErrorActionPreference = 'Continue'
$hText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $ScreenFirstFrames 2>&1 | Out-String
$hCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$hLines = @($hText -split "`r?`n")
if ($hCode -ne 0) { $failures.Add("run H: godot exited $hCode (expected 0)") }
$firstH = First $hLines '^SCREEN first open=1 key=2$'
if (-not $firstH) { $failures.Add("run H: no 'SCREEN first open=1 key=2' line (the launch did not open the between-runs screen first)") }
$startH = First $hLines '^(RUN start|BOSS armed|PULL spawn|ARENA error) '
if ($startH) { $failures.Add("run H: a run started (or failed) before any queue action: '$startH'") }

# ---- Run I: Enter after a wipe opens the screen, never a Clause-less restart (lane 3D-c) --------------------------
$ErrorActionPreference = 'Continue'
$iText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $WipeFrames '--' --press-enter 3 --pull 2 2>&1 | Out-String
$iCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$iLines = @($iText -split "`r?`n")
if ($iCode -ne 0) { $failures.Add("run I: godot exited $iCode (expected 0)") }
$firstI = First $iLines '^SCREEN first open=1 key=2$'
if (-not $firstI) { $failures.Add("run I: no 'SCREEN first open=1 key=2' line") }
$startsI = @($iLines | Where-Object { $_ -match '^RUN start ' })
$runEndI = First $iLines '^EVENT \{"result":"depleted",.*"type":"run_end"'
$reopenI = $null
$requeuedI = $null
if ($startsI.Count -lt 1 -or $startsI[0] -notmatch ' clause=(echo|tide|hush|pact) ') {
    $failures.Add("run I: the first run did not come from the screen's Queue with a Clause: '$($startsI | Select-Object -First 1)'")
}
if (-not $runEndI) {
    $failures.Add("run I: the party did not wipe (no depleted run_end EVENT) in $WipeFrames frames")
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
        } elseif ($requeuedI -notmatch ' clause=(echo|tide|hush|pact) ') {
            $failures.Add("run I: the run after the wipe has no Clause (it did not come through the screen): '$requeuedI'")
        }
    }
}

# ---- Run J: a human G-ping reaches the bot party (lane 3H-c) ------------------------------------------------------
$ErrorActionPreference = 'Continue'
$jText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $PingFrames '--' --press-enter 1 --press-ping $PingAt 2>&1 | Out-String
$jCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$jLines = @($jText -split "`r?`n")
if ($jCode -ne 0) { $failures.Add("run J: godot exited $jCode (expected 0)") }
$inputJ = First $jLines '^INPUT ping tick=\d+$'
$routedJ = $null
$reactJ = $null
if (-not $inputJ) {
    $failures.Add("run J: no 'INPUT ping tick=T' line (the scripted G was never pushed by frame $PingFrames)")
} else {
    $pressTick = [int]($inputJ -replace '^INPUT ping tick=', '')
    $iIn = [array]::IndexOf($jLines, $inputJ)
    $afterIn = @($jLines[($iIn + 1)..($jLines.Count - 1)])
    $routedJ = First $afterIn '^PING routed '
    if (-not $routedJ) {
        $failures.Add('run J: the G press reached no bot (no "PING routed" line: nothing routes PlayerInput.Pinged)')
    } elseif ($routedJ -notmatch '^PING routed kind=spot target=none tick=(\d+)') {
        $failures.Add("run J: wrong routed ping: '$routedJ' (want 'PING routed kind=spot target=none tick=N')")
    } else {
        $pingTick = [int]$Matches[1]
        if ($pingTick -lt $pressTick) { $failures.Add("run J: the ping was routed at tick $pingTick, before the press at $pressTick") }
        $iRt = [array]::IndexOf($afterIn, $routedJ)
        $afterRt = @($afterIn[($iRt + 1)..($afterIn.Count - 1)])
        $reactJ = First $afterRt '^PING react actor=[23] kind=move tick=\d+$'
        if (-not $reactJ) {
            $failures.Add('run J: no bot seat reacted to the routed ping (no "PING react actor=2|3 kind=move" line)')
        } else {
            $reactTick = [int]($reactJ -replace '^.* tick=', '')
            if ($reactTick -lt $pingTick -or $reactTick -gt ($pingTick + 40)) {
                $failures.Add("run J: the bot reaction at tick $reactTick is outside the ping's 40-tick move window from $pingTick")
            }
        }
    }
}

# ---- Run K: record an interactive run's keys, replay them through the real input path (lane 3E-b) -----------------
$kTag = [Guid]::NewGuid().ToString('N')
$kTemp = [IO.Path]::GetTempPath()
$recPath = Join-Path $kTemp "keyrun-hud-record-$kTag.input-log.json"
$k1LogPath = Join-Path $kTemp "keyrun-hud-k1-$kTag.txt"
$k2LogPath = Join-Path $kTemp "keyrun-hud-k2-$kTag.txt"
$examplePath = Join-Path $repo 'game/fixtures/input/example-enter-ping.input-log.json'

# Marker lines of a --log file, each as "<tick>|<line>" (tick = the last TICK line before it; none yet = -1).
function Get-Markers([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $tick = -1
    $out = @()
    foreach ($l in [IO.File]::ReadAllLines($path)) {
        if ($l -match '^TICK (\d+) ') { $tick = [int]$Matches[1]; continue }
        if ($l -match '^(RUN start |SCREEN reopen |PING routed )' -or $l -match '^EVENT \{.*"type":"run_end"') { $out += "$tick|$l" }
    }
    return $out
}

$ErrorActionPreference = 'Continue'
$k1Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $RecordFrames '--' --record-input $recPath --press-enter 3 --press-ping 2 --pull 2 --log $k1LogPath 2>&1 | Out-String
$k1Code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$k1Lines = @($k1Text -split "`r?`n")
if ($k1Code -ne 0) { $failures.Add("run K1: godot exited $k1Code (expected 0)") }
$recDone = First $k1Lines '^INPUT record done events=\d+ path=.+$'
$recEvents = @()
$recN = -1
if (-not $recDone) {
    $failures.Add("run K1: no 'INPUT record done events=N path=P' line (--record-input wrote no input log)")
} else {
    $recN = [int]($recDone -replace '^INPUT record done events=(\d+) .*$', '$1')
    if ($recN -lt 2) { $failures.Add("run K1: the input log holds $recN events (want >= 2: the Enters and the G)") }
    if (-not (Test-Path -LiteralPath $recPath)) {
        $failures.Add("run K1: '$recDone' but no file at $recPath")
    } else {
        $rec = Get-Content -Raw -LiteralPath $recPath | ConvertFrom-Json
        if ($rec.schema -ne 'keyrun.input-log/1') { $failures.Add("run K1: input log schema is '$($rec.schema)' (want keyrun.input-log/1)") }
        if ($rec.seed -ne 1 -or $rec.key -ne 2) { $failures.Add("run K1: input log header seed=$($rec.seed) key=$($rec.key) (want seed=1 key=2)") }
        $recEvents = @($rec.events)
        if ($recEvents.Count -ne $recN) { $failures.Add("run K1: the log line says $recN events, the file holds $($recEvents.Count)") }
        for ($i = 1; $i -lt $recEvents.Count; $i++) {
            if ([int]$recEvents[$i].tick -lt [int]$recEvents[$i - 1].tick) { $failures.Add("run K1: event $i tick $($recEvents[$i].tick) is before event $($i - 1)'s $($recEvents[$i - 1].tick)"); break }
        }
        if (@($recEvents | Where-Object { $_.key -eq 'Enter' -and $_.pressed }).Count -ne 3) { $failures.Add('run K1: the input log does not hold the three scripted Enter presses') }
        if (@($recEvents | Where-Object { $_.key -eq 'G' -and $_.pressed }).Count -ne 1) { $failures.Add('run K1: the input log does not hold the scripted G press') }
        if (-not (Test-Path -LiteralPath $examplePath)) {
            $failures.Add("run K1: no committed example at $examplePath")
        } elseif ([Convert]::ToBase64String([IO.File]::ReadAllBytes($recPath)) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($examplePath))) {
            $failures.Add("run K1: the recorded log differs from the committed example $examplePath (regenerate it from run K1 with a stated reason)")
        }
    }
}

$ErrorActionPreference = 'Continue'
$k2Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $RecordFrames '--' --replay-input $recPath --pull 2 --log $k2LogPath 2>&1 | Out-String
$k2Code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$k2Lines = @($k2Text -split "`r?`n")
if ($k2Code -ne 0) { $failures.Add("run K2: godot exited $k2Code (expected 0)") }
$replayed = @($k2Lines | Where-Object { $_ -match '^INPUT replay tick=\d+ key=\S+ pressed=(true|false)$' })
$replayDone = First $k2Lines '^INPUT replay done events=\d+ last=\d+$'
$scriptedK2 = First $k2Lines '^INPUT (enter|ping) '
if ($scriptedK2) { $failures.Add("run K2: a scripted press ran during the replay: '$scriptedK2'") }
if ($recEvents.Count -gt 0) {
    $want = @($recEvents | ForEach-Object { "INPUT replay tick=$($_.tick) key=$($_.key) pressed=$(([string]$_.pressed).ToLowerInvariant())" })
    if ($replayed.Count -ne $want.Count) {
        $failures.Add("run K2: $($replayed.Count) 'INPUT replay tick=' lines for $($want.Count) recorded events")
    } else {
        for ($i = 0; $i -lt $want.Count; $i++) {
            if ($replayed[$i] -ne $want[$i]) { $failures.Add("run K2: replay line $i is '$($replayed[$i])' (want '$($want[$i])')"); break }
        }
    }
    $wantDone = "INPUT replay done events=$($recEvents.Count) last=$($recEvents[$recEvents.Count - 1].tick)"
    if ($replayDone -ne $wantDone) { $failures.Add("run K2: replay summary '$replayDone' (want '$wantDone')") }
}
$m1 = @(Get-Markers $k1LogPath)
$m2 = @(Get-Markers $k2LogPath)
if (@($m1 | Where-Object { $_ -match '\|SCREEN reopen ' }).Count -lt 1 -or @($m1 | Where-Object { $_ -match '\|PING routed ' }).Count -lt 1) {
    $failures.Add("run K1: the run log has no 'SCREEN reopen' and 'PING routed' markers to compare ($($m1.Count) markers)")
} elseif ($m2.Count -lt $m1.Count) {
    $failures.Add("run K2: the replayed run log has $($m2.Count) markers, K1's has $($m1.Count) (first K1 marker: '$($m1[0])')")
} else {
    for ($i = 0; $i -lt $m1.Count; $i++) {
        if ($m2[$i] -ne $m1[$i]) { $failures.Add("run K2: marker $i is '$($m2[$i])', K1 had '$($m1[$i])' (same line at the same tick expected)"); break }
    }
}
foreach ($p in @($recPath, $k1LogPath, $k2LogPath)) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p } }

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
Write-Output '--- run E consume, runs F and G (backfill), lane 3J ---'
foreach ($l in @($consumedE, $f.Abandon, $f.Add, $f.Pull, $refillF, $consumeF, $joinerActs, $g.Abandon, $g.Add, $g.Pull, $firstH, $startH)) {
    if ($l) { Write-Output $l }
}
Write-Output '--- run I (wipe -> screen), lane 3D-c ---'
foreach ($l in @($firstI) + $startsI + @($runEndI, $reopenI, $requeuedI)) {
    if ($l) { Write-Output $l }
}
Write-Output '--- run J (human G-ping -> bots), lane 3H-c ---'
foreach ($l in @($inputJ, $routedJ, $reactJ)) {
    if ($l) { Write-Output $l }
}
Write-Output '--- run K (record -> replay), lane 3E-b ---'
foreach ($l in @($recDone) + $replayed + @($replayDone) + $m1) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "HudSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "HudSceneTest: PASS ticks=$Ticks pingTicks=$PingTicks partyTicks=$PartyTicks bossTicks=$BossTicks queueTicks=$QueueTicks backfillTicks=$BackfillTicks abandonSeat=$AbandonSeat wipeFrames=$WipeFrames pingAt=$PingAt pingFrames=$PingFrames recordFrames=$RecordFrames recordEvents=$recN"
exit 0
