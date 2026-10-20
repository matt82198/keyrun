<#
.SYNOPSIS
  Headless scene test for the 3C HUD (ENGINE-PLAN phase 3, lane 3C; BIBLE s7 choice of three, s8 Slack, s10 pings):
  party frames, objective line, Slack bar, SPREAD/STACK labels and the run-end panel on the real run root.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 twice and reads the HUD's own
  "HUD ..." lines from the engine output (the HUD prints what it draws when --autorun is on):

    Run A (pull 1, the Choir, solo Warden):  -- --autorun --ticks 600 --solo
      (600 since lane NAV-CORE, was 400: the Warden first walks from the Entrance into the Choir room, so the wipe's
      run_end moved from tick 374 to 413)
      1. exit 0 and "AUTORUN done ticks=600";
      2. "HUD party seat=1 name=Warden hp=H/400 res=R/M" (the party frame reads the human's health and resource);
      3. "HUD party seat=1 ... DOWNED" after the sim's death event for actor 1 (the DOWNED chip);
      4. "HUD objective pull=1/4 id=choir name="The Choir"" (the current pull's content name);
      5. "HUD slack ticks=620 of=620" first (Timer.InitialSlackTicks(2) = 600 + 20), then a lower value after the
         death (the sim spends 300 on a death and 500 on the wipe);
      6. "HUD runend result=depleted choices=3 cleared=0 ... title="Party wiped on pull 1 of 4 -- key stays at +2"" (the
         sim reports a lasting wipe as "depleted"; the panel must not call it cleared, and names the pull it was lost
         on, numbered as the objective line numbers it: lane 3C-d), three "HUD runend choice n=<1..3> slot=<slot>
         tier=<t> facet=... ability=warden.<id> charges=<c> id=reward-<slot>-warden.<slot>.<facet>-t<t>-<i>" lines
         (Rewards.RollRewardChoices over the Warden's content facet pool, lane 3C-d: no placeholder "ability-<n>"
         id), and "HUD runend cause seat=1" lines naming only seat 1's own causes; every "HUD runend result=" line of
         every autorun (A-G) titles a cleared=1 run "Run cleared -- ..." and a cleared=0 depleted run "Party wiped
         on pull <n> of <total> -- key stays at +<key>" (an untimed key keeps its level: Matt 2026-10-20, f-7c7abdf4;
         "key depleted" is banned from every title) (run C clears the dungeon since lane TUNE-BOSS, so the clear title
         is checked on it; the count of cleared runs is printed).
    Run B (from pull 2, the Pair, solo Warden):  -- --autorun --ticks 600 --pull 2 --solo
      (600 since lane NAV-CORE, was 400: the Pair now spawns at the far side of its hall, 7 m off, run_end at tick 455)
      7. "HUD objective pull=2/4 id=pair name="The Pair"", and the wipe title "Party wiped on pull 2 of 4 -- key
         stays at +2" (a later --pull start still numbers the pull in the full dungeon, as the objective line does);
      8. NO "HUD ping label=SPREAD" or "HUD ping label=STACK" line (SLAM-TELEGRAPH, wrong data shown is P0). The
         solo Warden is down before the Pair's first mark (its telegraph is due about tick 295), so the mark cast
         has no target and never starts; a spansCast telegraph is drawn only for a cast that will start
         (Scripts.WillStart), so the floor and the HUD must not call SPREAD/STACK for it. Before SLAM-TELEGRAPH this
         check asserted the opposite: a ping from the telegraph the sim drew after run_end for that dead-target cast.
         Run C (check 11) proves the live-run call.
    Run C (pull 1, the three-seat bot party, lane 3H), twice with the same seed:  -- --autorun --ticks <PartyTicks>
        (default 7000; the header said 3700, the value before lane TUNE-BOSS made the run reach the boss)
      9. "HUD party seat=1 name=Warden", "seat=2 name=Chanter", "seat=3 name=Cinder" (BotCommandSource drives 2, 3);
     10. in pull 1 (before the Pair spawns): a Chanter heal on a party seat and a Cinder damage hit on an enemy (at
         seed 1 the 3B healer role spends every GCD on heals and never casts Dissonance; the Cinder kit has no heal);
     11. the party reaches the Pair or run_end, is not wiped by tick 295, and a "HUD ping label=SPREAD|STACK" line
         appears before the party's third death (a call during a live run);
     12. a bot seat takes damage and every "HUD runend cause" line is seat 1's (the private run log's seat filter
         with more than one seat in the sim), and the party clears the dungeon (lane TUNE-BOSS, STATE.md D14): a
         "PULL spawn index=3 id=boss actors=900:hollowWarden.boss" line and a lockIndex 1 "lock_open" EVENT before
         run_end, and the run-end panel titled "Run cleared -- within par" (run_end timed at tick 3430 at seed 1 key 2 per the
         game-e2e log of run 39318059212 on main, 2026-10-19; the PASS line does not print run_end; was 3457
         since lane WARDEN-ENGAGES, 5168 since lane NAV-CORE, 6787 on the flat arena); the autorun Warden lands damage
         on the Choir at least every 300 ticks from pull_start to pull_clear (WardenEngagesTest.ps1, lane WARDEN-ENGAGES);
     13. "BOTS done commands=N hash=H" with N > 0, equal across the two runs (bot determinism);
     13a. lane NAV-CORE (STATE.md D18, the rooms-and-corridors route): the Warden's "room_enter" EVENTs reach
         choir-room, pair-hall, seed-chamber and warden-arena in that order, and each room's pull spawns ("PULL spawn
         index=i") only after the Warden entered that room (never at run start, never on the previous clear);
     13b. both "boss_add_spawn" EVENTs (901 twinA, 902 twinB, boss 900) before run_end: the Sundered Twins spawn in the
         full run, not only in run D (STATE.md NEXT ORDER step 3).
     13c. lane HUD-DECLUTTER (src/Hud/HudDeclutterTest.ps1 on run C's first run): party casts show in the party
         frames and never over a party member's head, only seat 1's damage spawns a world number, name plates draw
         the name only, a target frame and a boss frame carry enemy health, and enemy cast bars stay over the head
         and on the unit frame. Before HUD-DECLUTTER none of its proof lines exist: 13c fails (RED).
    Run D (lane 3I, the armed boss; from pull 4, the boss pull, three-seat party, key 10 seed 7):
        -- --autorun --ticks 600 --pull 4 --key 10 --seed 7
     14. "BOSS armed twins=2 lockAt=50" before the first TICK line (HostRunSetup builds the BossConfig from
         content/data/enemies/hollow-warden.json and SimDriver arms every run); runs A and C print it too (the --solo
         path and the pull-1 party path arm the same way);
     15. "PULL spawn index=0 id=boss actors=900:hollowWarden.boss";
     16. two "boss_add_spawn" EVENTs, 901 hollowWarden.twinA and 902 hollowWarden.twinB, both for boss 900 (the Sundered
         Twins exist only when the encounter is armed: an unarmed run never emits it).
         Why --pull 4: key 10 does not reach the boss from pull 1 under autorun. Lane 3I swept keys 2, 5, 8, 10, 12,
         15, 20 at seeds 1 and 7 for 8000 ticks: every run wiped in pull 2 (the Pair, run_end depleted at tick
         3611-5381), because the human seat is PlayerInput's ScriptedWarden, not the 3B tank bot that 3F's k10 s7
         probe used. Since lane TUNE-BOSS (STATE.md D14) every swept key (2, 5, 8, 10 at seeds 1 and 7) clears from
         pull 1 (the D14 sweep, end ticks 6787 to 8603; re-swept on the lane NAV-CORE route, see its PR); run D keeps
         --pull 4 because it proves the armed boss at key 10 in 600 ticks, not the route.
    Run E (lane 3I, the queued run; solo Warden, as ScreenSceneTest):  -- --autorun --ticks 600 --solo --requeue
        (--requeue: an autorun starts the queued run only when asked, so runs A-D and the other scene tests stay one
        run each; an interactive game always starts it; 480 -> 600 by ROOM-LOCK-IN D27: the solo Warden now dies at 390
        and the depleted run_end lands at 491, past the old 480)
     17. the first "RUN start" says "key=2 clause=none" (no queue yet: today's run);
     18. "SCREEN queue dungeon=hollow-vault key=3 clause=<id>" (the screen's autorun script queues key 3 + Clause 2);
     19. after it, a second "RUN start ... key=3 clause=<that id>" (the key is the run content's KeyLevel and the
         Clause is the sim's EncounterRules.ActiveClauseId, so a request that is dropped on the way reads key=2 or
         clause=none), followed by a second "BOSS armed" and "PULL spawn index=0 id=choir";
     20. then "QUEUE consume n=1 size=0" (lane 3J: the request that started the run leaves the Queue node, so it can
         never also refill a seat).
    Run F (lane 3J, backfill in the real loop; three-seat party, key 2, no --requeue):
        -- --autorun --ticks 3500 --abandon-seat <AbandonSeat> --joiner-key 2
        (3500 since PARTY-TOGETHER, was 3300: the joiner now walks into the Pair hall before it casts; on seed 1 key 2
        the refilled Chanter enters pair-hall at tick 3268 and first casts at 3345)
        (--abandon-seat A@T: SimDriver calls AbandonSeat(A) at run tick T and, with --joiner-key K, adds one
        QueueRequest at key K straight to the Queue node at Hud/Screen/Queue; this is the scripted path, the screen's
        Queue button is not pressed mid-run)
     21. "SEAT abandon actor=A tick=T" in pull 1, then "QUEUE add dungeon=hollow-vault key=2";
     22. the party survives to "PULL spawn index=1 id=pair", and in that same step (no TICK line between them)
         "SEAT refill actor=A tick=X key=2" with X = that step's TICK tick + 1 (Backfill.Step's FillTick: the first
         tick whose commands are the joiner's), then "QUEUE consume n=1 size=0";
     23. the open seat stands idle: no cast_complete EVENT with "actorId":A between the abandon and the refill, the
         refilled seat casts again after it (the joiner's commands run from the pull start), and no party seat dies
         before pull 2. Default A@T = 2@1500 since lane WARDEN-ENGAGES (the Chanter leaves at tick 1500, late in the
         Choir fight; the Warden now fights the Choir melee after the kick, so seed 1 key 2 clears the Choir at 1757 and
         pull 2 starts at tick 1840 with all three seats alive; 2@3000 from lane NAV-CORE now falls in pull 2; 2@1500 -> 2@1700
         by ROOM-LOCK-IN D27 (Choir pull_start 50, room locked): a Chanter leaving at 1500 lets the Warden die at 1794, before
         the 1809 clear; at 1700 the Choir clears at 1749, pull 2 starts at 1844 and the seat refills at 1844). The old 1@1000 cannot work on the
         route: the next pull spawns only when a party member walks into its room, and an abandoned human leader stands
         idle, so his bots never travel; 3@1000 to 3@3000 leave the Choir uncleared without the Cinder's damage.
    Run G (lane 3J, key mismatch): as F with --joiner-key 3
     24. "SEAT abandon", "QUEUE add ... key=3" and "PULL spawn index=1" appear, but no "SEAT refill" line and no
         "QUEUE consume" line (Backfill.Admits is exact key equality: a key-3 joiner is refused a key-2 run).
  Before 3I the arena never arms the boss and never reads the queue: 14-19 fail (RED). Before 3J SimDriver steps
  RunRoot.StepRun and ignores --abandon-seat: 20-24 fail (RED).
    Source checks (lane HUD-FU; the ability bar draws with immediate-mode DrawString, so no node or line shows it):
     25. game/src/Hud/AbilityBar.cs draws no run-over banner: no "Run over" text and no run-end result, result type or
         ToJs() in any DrawString/DrawCentered/DrawMeter call (since 3C the run-end panel owns the run-over state), its
         ShowsOutOfRange call passes a runOver argument (lane TINT-RUNOVER: the bar may read THAT the run ended, to turn
         the range tint off; until then the clause banned any RunEnd read, which only the stale banner made), and
         RunEndPanel.cs still draws "Enter starts a fresh run";
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
    Run I (lane 3D-c, a wipe goes back to the screen; interactive, three-seat party, from pull 2 so it wipes on the Pair):
        -- --press-enter 3 --pull 2 --bots-hold   (--quit-after <WipeFrames>)
        (--pull 2 since lane NAV-CORE, was --pull 4: a --pull N run now starts in its room with the boss 7 m off, and
        the two bots kill the boss over the idle human, so the run clears instead of wiping; from pull 2 it wiped at
        tick 896)
        (--bots-hold since PARTY-TOGETHER: the bots now walk into the fight's room and clear the Pair alone over the
        idle human (pull_clear 1429), and no idle-human start wipes any more; held bots spawn but take no action, so
        the wipe is real because nobody fights: seats die at 207, 354, 501, depleted run_end at tick 602. This run exercises the depleted run_end -> screen path, not whether
        bots can win alone)
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
    Run K (lane 3E-b, a recorded input log replays through the real input path; interactive, three-seat party; from
        pull 4, the boss fight; since lane NAV-CORE the bots clear it over the idle human, so its run_end is timed, and
        the fixture's post-run Enter tick moved with it):
      K1  -- --record-input <log> --press-enter 3 --press-ping 2 --pull 4 --log <k1>   (--quit-after <RecordFrames>)
      K2  -- --replay-input <log> --pull 4 --log <k2>                                  (--quit-after <RecordFrames>)
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
    Run L (lane 3E-c, the camera yaw a human sets is recorded beside the keys and replayed; interactive, three-seat party):
      L1  -- --replay-input <walk> --record-input <log> --press-yaw <YawAt>:<YawTo> --log <l1>   (--quit-after <YawFrames>)
      L2  -- --replay-input <log> --log <l2>                                                     (--quit-after <YawFrames>)
        (<walk> is a keyrun.input-log/1 file this script writes: Enter at tick 0, the screen's Queue, and W held from tick 1,
        so the Warden walks through the existing replay seam; --press-yaw T:Y turns the CameraRig to Y radians through
        CameraRig.Look, the mouse handler's own call, once the run reaches tick T)
     33. L1: exit 0, "INPUT yaw tick=T yaw=Y", "INPUT record done events=N cam=M path=<log>" with M >= 1, and the file
         parses as keyrun.input-log/2 (seed 1, key 2) with M camera events, the first at or after <YawAt>, and W pressed
         among its events; it equals the committed example game/fixtures/input/example-yaw-walk.input-log.json byte for
         byte; the Warden's x in the L1 run log falls by more than 100 mm within 20 ticks of the yaw event (W walks along
         the turned camera);
     34. L2: exit 0, M "INPUT replay cam tick=T yaw=Y" lines matching the log's camera events in order, then
         "INPUT replay done events=N last=T cam=M", and no scripted "INPUT enter"/"INPUT ping"/"INPUT yaw";
     35. every L1 "TICK n warden=(x,z)" line at or after the yaw event's tick appears in L2's run log identically (at
         least 20 of them): position equality is the proof the replayed yaw is the recorded one (positions are integer
         mm, so there is no float noise to round). Before 3E-c SimDriver ignores --press-yaw and records no camera: no
         "INPUT yaw" or "cam=" line and 33 fails (RED).
    Run M (lane HOST-HEADING, a recorded human run starts facing the route and the input log carries that heading):
      M1  -- --record-input <log> --press-enter 1 --log <m1>       (--quit-after <HeadingFrames>)
      M2  -- --replay-input <log> --log <m2>                       (--quit-after <HeadingFrames>)
      M3  -- --replay-input <walk> --log <m3>                      (<walk>: M1's startHeading, Enter at 0, W from 1)
      M4  -- --replay-input <bad> --log <m4>                       (<bad>: "startHeading": "east")
     36. M1: exit 0, a "CAMERA start heading=(dx,dz) yaw=Y" line WITHOUT "reason=input-log", and the file is
         keyrun.input-log/1 (the heading is not a camera event) whose "startHeading" equals Y and faces +x (-Pi/2);
     37. M2: exit 0, "INPUT replay heading yaw=Y" with M1's Y, and every TICK warden line both logs hold is identical
         (at least 20);
     38. M3: exit 0, "INPUT replay heading yaw=Y", and W walks the Warden down +x (x up by more than 100 mm in 20 ticks,
         z within 100 mm): the replayed heading is the one movement reads;
     39. M4: exit 1 with an "ARENA error" naming startHeading (the reader fails closed on a mistyped heading).
     Before HOST-HEADING a recording keeps the legacy heading (reason=input-log), writes no startHeading, and a replay
     ignores the field: 36-39 fail (RED). -HeadingOnly runs the source checks and run M alone.
     Run K1's byte comparison with the committed example ignores the one "startHeading" header line (the example
     predates the field and fixtures are not regenerated); K1 asserts that line separately.
    Run A also carries the ability bar icons (lane ABILITY-ICONS; the bar prints one line per slot under --autorun):
     40. nine "HUD bar slot=<key> ability=warden.<id> icon=res://assets/kenney-board-game-icons/<file>.png tex=64x64"
         lines, one per slot 1-5, Q, E, R, F (tex is the loaded Texture2D's size, so a missing file or an unmapped id
         reads icon=none tex=none and fails). Before ABILITY-ICONS the bar draws only text: no "HUD bar" line, 40 RED.
    Run N (lane TINT-RUNOVER, a non-clear run end with a living out-of-range target; three-seat party, key 2):
        -- --autorun --ticks <TintTicks> --abandon-seat <TintAbandon>   (default 1400 and 2@600, no --joiner-key)
        (the bar prints "HUD bar range tinted=N over=0|1 tick=T" under --autorun whenever the count of range-tinted
        slots or the run-over state changes. At seed 1 the Chanter leaves at 600, the Warden dies at 1014 with the
        Choir enemy it targeted alive and out of range, the bar tints 4 slots at 1025, and the party wipes: run_end
        depleted at 1271)
     41. a "HUD runend result=depleted ... cleared=0" line (the run did not clear), at least one "HUD bar range
         tinted=N over=0" line with N > 0 (control: the live run tints), at least one "over=1" line, and every
         "over=1" line reads tinted=0. Before TINT-RUNOVER the bar passed no runOver and printed "tinted=4 over=1
         tick=1271": 41 RED.

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/HudSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
#>
param(
    [int]$Ticks = 600,
    [int]$PingTicks = 600,
    [int]$PartyTicks = 7000,
    [int]$BossTicks = 600,
    [int]$QueueTicks = 600,
    [int]$BackfillTicks = 3500,
    [string]$AbandonSeat = '2@1700',
    [int]$ScreenFirstFrames = 240,
    [int]$WipeFrames = 6600,
    [int]$PingAt = 100,
    [int]$PingFrames = 900,
    [int]$RecordFrames = 6600,
    [int]$YawAt = 60,
    [string]$YawTo = '1.5',
    [int]$YawFrames = 900,
    [int]$HeadingFrames = 420,
    [int]$TintTicks = 1400,
    [string]$TintAbandon = '2@600',
    [switch]$HeadingOnly
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$wrapper = Join-Path $repo 'tools/godot/godot.ps1'
$failures = New-Object System.Collections.Generic.List[string]

# One launch lock for the whole sequence (tools/godot/launch-lock.ps1): no other launch can interleave, and every
# launch below runs one after another through godot.ps1. Exit 2 when another Godot instance holds the lock.
. (Join-Path $repo 'tools/godot/launch-lock.ps1')
$launchLock = Enter-KeyrunLaunchLock -Owner 'HudSceneTest.ps1' -ExitCodeOnBusy 2
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
if ($barSrc -match 'Draw(String|Centered|Meter)\([^;]*(RunEnd|RunResult|\.ToJs\(\))') { $failures.Add('AbilityBar.cs draws run-over text (the run-end panel owns the result, rewards and Enter hint)') }
if ($barSrc -notmatch 'ShowsOutOfRange\([^;]*\brunOver:') { $failures.Add('AbilityBar.cs calls ShowsOutOfRange without a runOver argument (the range tint must be off once the run has ended, lane TINT-RUNOVER)') }
if ($panelSrc -notmatch 'Enter starts a fresh run') { $failures.Add('RunEndPanel.cs no longer draws "Enter starts a fresh run" (the only Enter hint once the bar banner is gone)') }
if ($screenSrc -match 'does not read the queued') { $failures.Add('Screen.cs still says the host does not read the queued key and Clause (false since 3I/3J)') }

# ---- Run M: a recorded human run starts facing the route; the input log carries the heading (lane HOST-HEADING) ---
# The "startHeading" header value of an input log file, as its text, or $null.
function Get-StartHeadingText([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $hit = [regex]::Match([IO.File]::ReadAllText($path), '(?m)^  "startHeading": ([^,\r\n]+),$')
    if ($hit.Success) { return $hit.Groups[1].Value }
    return $null
}

# One interactive launch for run M: exit code and output lines.
function Invoke-Interactive([int]$frames, [string[]]$userArgs) {
    $ErrorActionPreference = 'Continue'
    $text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $frames '--' @userArgs 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($rc -eq 2 -and $text -match 'Godot binary not found') {
        Write-Output $text
        Write-Output 'HudSceneTest: CANNOT RUN (Godot binary not found)'
        exit 2
    }
    return [pscustomobject]@{ Code = $rc; Lines = @($text -split "`r?`n") }
}

# "TICK n warden=(x,z)" lines of a --log file by tick (the first run's line for each tick).
function Get-WardenTickLines([string]$path) {
    $out = New-Object 'System.Collections.Generic.Dictionary[int,string]'
    if (-not (Test-Path -LiteralPath $path)) { return ,$out }
    foreach ($l in [IO.File]::ReadAllLines($path)) {
        if ($l -match '^(TICK (\d+) warden=\((-?\d+),(-?\d+)\))' -and -not $out.ContainsKey([int]$Matches[2])) { $out[[int]$Matches[2]] = $Matches[1] }
    }
    return ,$out
}

function Invoke-RunM {
    $shown = New-Object System.Collections.Generic.List[string]
    $tag = [Guid]::NewGuid().ToString('N')
    $tmp = [IO.Path]::GetTempPath()
    $mRec = Join-Path $tmp "keyrun-hud-heading-$tag.input-log.json"
    $mWalk = Join-Path $tmp "keyrun-hud-heading-walk-$tag.input-log.json"
    $mBad = Join-Path $tmp "keyrun-hud-heading-bad-$tag.input-log.json"
    $m1Log = Join-Path $tmp "keyrun-hud-m1-$tag.txt"
    $m2Log = Join-Path $tmp "keyrun-hud-m2-$tag.txt"
    $m3Log = Join-Path $tmp "keyrun-hud-m3-$tag.txt"
    $m4Log = Join-Path $tmp "keyrun-hud-m4-$tag.txt"
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $inv = [Globalization.CultureInfo]::InvariantCulture

    # 36. M1: a plain recording heads down the route and writes that heading.
    $m1 = Invoke-Interactive $HeadingFrames @('--record-input', $mRec, '--press-enter', '1', '--log', $m1Log)
    if ($m1.Code -ne 0) { $failures.Add("run M1: godot exited $($m1.Code) (expected 0)") }
    $head1 = First $m1.Lines '^CAMERA start heading'
    $headYaw = $null
    if (-not $head1) {
        $failures.Add("run M1: no 'CAMERA start heading' line")
    } elseif ($head1 -match 'reason=input-log') {
        $failures.Add("run M1: '$head1' (a --record-input launch kept the legacy heading instead of facing the route)")
    } elseif ($head1 -match ' yaw=(\S+)$') {
        $headYaw = $Matches[1]
    }
    [void]$shown.Add($head1)
    $sh1 = Get-StartHeadingText $mRec
    if (-not (Test-Path -LiteralPath $mRec)) {
        $failures.Add("run M1: --record-input wrote no file at $mRec")
    } else {
        $recM = Get-Content -Raw -LiteralPath $mRec | ConvertFrom-Json
        if ($recM.schema -ne 'keyrun.input-log/1') { $failures.Add("run M1: input log schema is '$($recM.schema)' (want keyrun.input-log/1: the start heading is a header field, not a camera event)") }
        if ($null -eq $sh1) {
            $failures.Add("run M1: the input log has no `"startHeading`" header line")
        } else {
            [void]$shown.Add("M1 startHeading=$sh1")
            $shF = [single]::Parse($sh1, $inv)
            if ($headYaw -and $shF -ne [single]::Parse($headYaw, $inv)) { $failures.Add("run M1: startHeading $sh1 is not the camera's start yaw $headYaw") }
            if ([math]::Abs($shF + [math]::PI / 2) -gt 0.001) { $failures.Add("run M1: startHeading $sh1 does not face +x (want -Pi/2)") }
        }
    }

    # 37. M2: replaying M1's file restores the heading and gives the same TICK lines.
    $m2 = Invoke-Interactive $HeadingFrames @('--replay-input', $mRec, '--log', $m2Log)
    if ($m2.Code -ne 0) { $failures.Add("run M2: godot exited $($m2.Code) (expected 0)") }
    $rh2 = First $m2.Lines '^INPUT replay heading yaw=\S+$'
    [void]$shown.Add($rh2)
    if (-not $rh2) {
        $failures.Add("run M2: no 'INPUT replay heading yaw=Y' line (the replay did not apply the file's startHeading)")
    } elseif ($sh1 -and $rh2 -ne "INPUT replay heading yaw=$sh1") {
        $failures.Add("run M2: '$rh2' (want 'INPUT replay heading yaw=$sh1')")
    }
    $t1 = Get-WardenTickLines $m1Log
    $t2 = Get-WardenTickLines $m2Log
    $comparedM = 0
    foreach ($t in @($t1.Keys | Sort-Object)) {
        if (-not $t2.ContainsKey($t)) { continue }
        if ($t2[$t] -ne $t1[$t]) { $failures.Add("run M2: '$($t2[$t])' but M1 had '$($t1[$t])'"); break }
        $comparedM++
    }
    if ($comparedM -lt 20) { $failures.Add("run M2: only $comparedM TICK warden lines compared between M1 and M2 (want >= 20)") }
    [void]$shown.Add("M2 compared=$comparedM")

    # 38. M3: the recorded heading is the one W walks along (+x down corridor 1).
    $walkHeading = if ($sh1) { $sh1 } else { '-1.5707964' }
    $walk = "{`n  `"schema`": `"keyrun.input-log/1`",`n  `"seed`": 1,`n  `"key`": 2,`n  `"startHeading`": $walkHeading,`n  `"events`": [`n" +
        "    { `"tick`": 0, `"key`": `"Enter`", `"pressed`": true },`n    { `"tick`": 1, `"key`": `"W`", `"pressed`": true }`n  ]`n}`n"
    [IO.File]::WriteAllText($mWalk, $walk, $utf8)
    $m3 = Invoke-Interactive $HeadingFrames @('--replay-input', $mWalk, '--log', $m3Log)
    if ($m3.Code -ne 0) { $failures.Add("run M3: godot exited $($m3.Code) (expected 0)") }
    $rh3 = First $m3.Lines '^INPUT replay heading yaw=\S+$'
    if ($rh3 -ne "INPUT replay heading yaw=$walkHeading") { $failures.Add("run M3: heading line '$rh3' (want 'INPUT replay heading yaw=$walkHeading')") }
    $t3 = Get-WardenTickLines $m3Log
    $first3 = @($t3.Keys | Where-Object { $_ -ge 1 } | Sort-Object | Select-Object -First 1)
    if ($first3.Count -lt 1 -or -not $t3.ContainsKey($first3[0] + 20)) {
        $failures.Add("run M3: the run log has no TICK warden lines 20 ticks apart after the W press")
    } else {
        $a3 = $t3[$first3[0]]
        $b3 = $t3[$first3[0] + 20]
        [void]$shown.Add("M3 $a3 -> $b3")
        $null = $a3 -match 'warden=\((-?\d+),(-?\d+)\)'; $ax = [int]$Matches[1]; $az = [int]$Matches[2]
        $null = $b3 -match 'warden=\((-?\d+),(-?\d+)\)'; $bx = [int]$Matches[1]; $bz = [int]$Matches[2]
        if ($bx -le ($ax + 100) -or [math]::Abs($bz - $az) -gt 100) { $failures.Add("run M3: W moved the Warden $a3 -> $b3 (want x up by more than 100 mm, z within 100 mm: down +x)") }
    }

    # 39. M4: a mistyped startHeading is refused.
    [IO.File]::WriteAllText($mBad, ($walk -replace '"startHeading": [^,]+,', '"startHeading": "east",'), $utf8)
    $m4 = Invoke-Interactive 120 @('--replay-input', $mBad, '--log', $m4Log)
    # The error comes on stderr, which PowerShell may wrap across lines: match it with the whitespace taken out.
    $err4 = $null
    if ((($m4.Lines -join '') -replace '\s', '') -match 'ARENAerror:[^\r\n]*?startHeadingmustbeafinitenumber') { $err4 = 'ARENA error ... startHeading must be a finite number' }
    [void]$shown.Add("M4 exit=$($m4.Code) $err4")
    if ($m4.Code -ne 1 -or -not $err4) { $failures.Add("run M4: exit $($m4.Code), error '$err4' (want exit 1 and an 'ARENA error' naming startHeading)") }

    foreach ($p in @($mRec, $mWalk, $mBad, $m1Log, $m2Log, $m3Log, $m4Log)) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p } }
    return ,$shown.ToArray()
}

$mShown = @(Invoke-RunM)
if ($HeadingOnly) {
    Write-Output '--- run M (recorded start heading), lane HOST-HEADING ---'
    foreach ($l in $mShown) { if ($l) { Write-Output $l } }
    if ($failures.Count -gt 0) {
        Write-Output "HudSceneTest: FAIL ($($failures.Count))"
        $failures | ForEach-Object { Write-Output "  - $_" }
        exit 1
    }
    Write-Output "HudSceneTest: PASS (run M only) headingFrames=$HeadingFrames"
    exit 0
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

$runEnd = First $a.Lines '^HUD runend result=depleted choices=3 cleared=0 tick=\d+ title="Party wiped on pull 1 of 4 -- key stays at \+2"$'
if (-not $runEnd) { $failures.Add("run A: no 'HUD runend result=depleted choices=3 cleared=0 ... title=""Party wiped on pull 1 of 4 -- key stays at +2""' line (got '$(First $a.Lines '^HUD runend result=')')") }
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

# ---- Run N (lane TINT-RUNOVER, check 41): the range tint is off once a non-clear run has ended -------------------
$tintN = Invoke-Arena $TintTicks @('--abandon-seat', $TintAbandon)
$tintRun = @(FirstRun $tintN.Lines)
if ($tintN.Code -ne 0) { $failures.Add("run N: godot exited $($tintN.Code) (expected 0)") }
$tintEnd = First $tintRun '^HUD runend result=depleted .*\bcleared=0 '
$tintLive = First $tintRun '^HUD bar range tinted=[1-9]\d* over=0 '
$tintOver = @($tintRun | Where-Object { $_ -match '^HUD bar range tinted=\d+ over=1 ' })
$tintStuck = $tintOver | Where-Object { $_ -notmatch '^HUD bar range tinted=0 over=1 ' } | Select-Object -First 1
if (-not $tintEnd) { $failures.Add("run N: no 'HUD runend result=depleted ... cleared=0' line (the party must wipe for the case; got '$(First $tintRun '^HUD runend result=')')") }
if (-not $tintLive) { $failures.Add('run N: no "HUD bar range tinted=N over=0" line with N > 0 (control: a live run with an out-of-range target tints)') }
if ($tintOver.Count -eq 0) { $failures.Add('run N: no "HUD bar range ... over=1" line (the bar never saw the run end)') }
if ($tintStuck) { $failures.Add("run N: the range tint still shows after the run ended: '$tintStuck'") }

# ---- Run B: from pull 2 -----------------------------------------------------------------------------------------
$b = Invoke-Arena $PingTicks @('--pull', '2', '--solo')
if ($b.Code -ne 0) { $failures.Add("run B: godot exited $($b.Code) (expected 0)") }
$objB = First $b.Lines '^HUD objective pull=2/4 id=pair name="The Pair"'
if (-not $objB) { $failures.Add('run B: no "HUD objective pull=2/4 id=pair name=""The Pair""" line') }
$ping = First $b.Lines '^HUD ping label=(SPREAD|STACK) '
if ($ping) { $failures.Add("run B: a SPREAD/STACK call for a mark cast that never starts (the solo Warden is dead, no target): '$ping'") }
# Lane 3C-d: the solo Warden wipes in the Pair; --pull 2 drops pull 1 from the run's dungeon, and the title still
# numbers the Pair 2 of 4 (ObjectiveModel numbering), never 1 of 3.
$runEndB = First $b.Lines '^HUD runend result=depleted choices=3 cleared=0 tick=\d+ title="Party wiped on pull 2 of 4 -- key stays at \+2"$'
if (-not $runEndB) { $failures.Add("run B: no 'HUD runend ... title=""Party wiped on pull 2 of 4 -- key stays at +2""' line (got '$(First $b.Lines '^HUD runend result=')')") }

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
# Lane COMBO-BLAME (BIBLE s4): the run-end panel's late-combo lines are seat 1's own only, one per distinct
# (windowId, reason) of the combo_denied EVENTs whose payoffActorId is 1, capped at 3.
foreach ($run in @(@{ Name = 'A'; Lines = $a.Lines }, @{ Name = 'C'; Lines = $c.Lines })) {
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
# Lane NAV-CORE 13a (STATE.md D18): the Warden walks the route room by room, and each pull spawns only once he is in.
$cRun = @(FirstRun $c.Lines)
$roomOrder = @('choir-room', 'pair-hall', 'seed-chamber', 'warden-arena')
$roomsC = @()
$iPrevRoom = -1
for ($r = 0; $r -lt $roomOrder.Count; $r++) {
    $enter = First $cRun "^EVENT \{`"actorId`":1,`"room`":`"$($roomOrder[$r])`",`"tick`":\d+,`"type`":`"room_enter`"\}$"
    $spawn = First $cRun "^PULL spawn index=$r "
    if (-not $enter) { $failures.Add("run C: the Warden never entered $($roomOrder[$r]) (no room_enter EVENT for actor 1)"); continue }
    $iEnter = [array]::IndexOf($cRun, $enter)
    if ($iEnter -le $iPrevRoom) { $failures.Add("run C: the Warden entered $($roomOrder[$r]) before the room ahead of it on the route") }
    $iPrevRoom = $iEnter
    if (-not $spawn) { $failures.Add("run C: no 'PULL spawn index=$r' line (the pull of $($roomOrder[$r]) never spawned)") }
    elseif ([array]::IndexOf($cRun, $spawn) -lt $iEnter) { $failures.Add("run C: pull $r spawned before the Warden entered $($roomOrder[$r]): '$spawn'") }
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
foreach ($f in @(& (Join-Path $PSScriptRoot 'WardenEngagesTest.ps1') -Lines $cRun -PullId 'choir' -MaxGap 300)) { $failures.Add("run C: $f") }
# 13c. Lane HUD-DECLUTTER: friendly casts in the party frames, only the human's damage numbers, enemy health in unit
# frames, enemy cast bars kept (src/Hud/HudDeclutterTest.ps1 holds the four checks and their RED on main).
foreach ($f in @(& (Join-Path $PSScriptRoot '../Hud/HudDeclutterTest.ps1') -Lines $cRun)) { $failures.Add("run C: $f") }
# 13d. Lane fix/target-frame-after-death (P0): a cleared run's objective counts the enemies still alive at run_end (a
# Sundered Twin that outlives the boss), never "No enemies remain" over a live target frame (src/Hud/ObjectiveClearTest.ps1).
foreach ($f in @(& (Join-Path $PSScriptRoot '../Hud/ObjectiveClearTest.ps1') -Lines $cRun)) { $failures.Add("run C: $f") }

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

# ---- Run-end titles across every autorun (lane 3C-d) ----------------------------------------------------------
# A cleared run keeps the clear title; a wipe names the pull it was lost on. Run C (and its repeat) clears the dungeon
# since lane TUNE-BOSS; the other autoruns wipe or time out of ticks: $clearedTitles says how many cleared.
$titleLines = @(@($a.Lines) + @($b.Lines) + @($c.Lines) + @($c2.Lines) + @($d.Lines) + @($e.Lines) + @($f.Lines) + @($g.Lines) | Where-Object { $_ -match '^HUD runend result=' })
$clearedTitles = 0
foreach ($t in $titleLines) {
    if ($t -match ' cleared=1 ') {
        $clearedTitles++
        if ($t -notmatch ' title="Run cleared -- (within par|key stays at \+\d+)"$') { $failures.Add("a cleared run lost the clear title: '$t'") }
    } elseif ($t -match '^HUD runend result=depleted ' -and $t -notmatch ' title="Party wiped on pull \d+ of \d+ -- key stays at \+\d+"$') {
        $failures.Add("a wiped run's title does not name the pull it was lost on and the key level it keeps: '$t'")
    }
    # An untimed key keeps its level (Matt 2026-10-20, f-7c7abdf4): no title may say "depleted", which a WoW player
    # reads as the key losing a level.
    if ($t -match ' title="[^"]*depleted') { $failures.Add("a run-end title says the key is depleted: '$t'") }
}
if ($titleLines.Count -eq 0) { $failures.Add('no "HUD runend result=" line in any autorun (the title checks did not run)') }

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
$iText = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $WipeFrames '--' --press-enter 3 --pull 2 --bots-hold 2>&1 | Out-String
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
$k1Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $RecordFrames '--' --record-input $recPath --press-enter 3 --press-ping 2 --pull 4 --log $k1LogPath 2>&1 | Out-String
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
        # HOST-HEADING: a plain recording carries one "startHeading" header line the committed example predates; it
        # is asserted here and left out of the byte comparison (the fixture is not regenerated).
        $kHeading = Get-StartHeadingText $recPath
        if ($null -eq $kHeading) { $failures.Add("run K1: the input log has no `"startHeading`" header line") }
        $recText = ([IO.File]::ReadAllText($recPath)) -replace '(?m)^  "startHeading": [^,\r\n]+,\n', ''
        if (-not (Test-Path -LiteralPath $examplePath)) {
            $failures.Add("run K1: no committed example at $examplePath")
        } elseif ($recText -cne [IO.File]::ReadAllText($examplePath)) {
            $failures.Add("run K1: the recorded log differs from the committed example $examplePath (regenerate it from run K1 with a stated reason)")
        }
    }
}

$ErrorActionPreference = 'Continue'
$k2Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $RecordFrames '--' --replay-input $recPath --pull 4 --log $k2LogPath 2>&1 | Out-String
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

# ---- Run L: the camera yaw is recorded beside the keys and replayed through the rig movement reads (lane 3E-c) -------
$lTag = [Guid]::NewGuid().ToString('N')
$walkPath = Join-Path $kTemp "keyrun-hud-walk-$lTag.input-log.json"
$lRecPath = Join-Path $kTemp "keyrun-hud-yaw-$lTag.input-log.json"
$l1LogPath = Join-Path $kTemp "keyrun-hud-l1-$lTag.txt"
$l2LogPath = Join-Path $kTemp "keyrun-hud-l2-$lTag.txt"
$yawExamplePath = Join-Path $repo 'game/fixtures/input/example-yaw-walk.input-log.json'
# L1's held key comes through the existing replay seam: Enter at tick 0 (the screen's Queue starts the run), W from tick 1.
$walkJson = "{`n  `"schema`": `"keyrun.input-log/1`",`n  `"seed`": 1,`n  `"key`": 2,`n  `"events`": [`n" +
    "    { `"tick`": 0, `"key`": `"Enter`", `"pressed`": true },`n    { `"tick`": 1, `"key`": `"W`", `"pressed`": true }`n  ]`n}`n"
[IO.File]::WriteAllText($walkPath, $walkJson, (New-Object System.Text.UTF8Encoding($false)))

# "TICK n warden=(x,z)" lines of a --log file by tick (the first run's line for each tick).
function Get-WardenTicks([string]$path) {
    $out = New-Object 'System.Collections.Generic.Dictionary[int,string]'
    if (-not (Test-Path -LiteralPath $path)) { return ,$out }
    foreach ($l in [IO.File]::ReadAllLines($path)) {
        if ($l -match '^(TICK (\d+) warden=\((-?\d+),(-?\d+)\))' -and -not $out.ContainsKey([int]$Matches[2])) { $out[[int]$Matches[2]] = $Matches[1] }
    }
    return ,$out
}

$ErrorActionPreference = 'Continue'
$l1Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $YawFrames '--' --replay-input $walkPath --record-input $lRecPath --press-yaw "${YawAt}:$YawTo" --log $l1LogPath 2>&1 | Out-String
$l1Code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$l1Lines = @($l1Text -split "`r?`n")
if ($l1Code -ne 0) { $failures.Add("run L1: godot exited $l1Code (expected 0)") }
$yawL1 = First $l1Lines '^INPUT yaw tick=\d+ yaw=\S+$'
if (-not $yawL1) { $failures.Add("run L1: no 'INPUT yaw tick=T yaw=Y' line (the --press-yaw seam never turned the camera)") }
$recDoneL = First $l1Lines '^INPUT record done events=\d+ cam=\d+ path=.+$'
$lEvents = @()
$camEvents = @()
$yawTick = -1
if (-not $recDoneL) {
    $failures.Add("run L1: no 'INPUT record done events=N cam=M path=P' line (the camera turn was not recorded)")
} else {
    $camN = [int]($recDoneL -replace '^INPUT record done events=\d+ cam=(\d+) .*$', '$1')
    if ($camN -lt 1) { $failures.Add("run L1: the input log holds $camN camera events (want >= 1: the scripted yaw)") }
    if (-not (Test-Path -LiteralPath $lRecPath)) {
        $failures.Add("run L1: '$recDoneL' but no file at $lRecPath")
    } else {
        $recL = Get-Content -Raw -LiteralPath $lRecPath | ConvertFrom-Json
        if ($recL.schema -ne 'keyrun.input-log/2') { $failures.Add("run L1: input log schema is '$($recL.schema)' (want keyrun.input-log/2)") }
        if ($recL.seed -ne 1 -or $recL.key -ne 2) { $failures.Add("run L1: input log header seed=$($recL.seed) key=$($recL.key) (want seed=1 key=2)") }
        $lEvents = @($recL.events)
        $camEvents = @($recL.camera)
        if ($camEvents.Count -ne $camN) { $failures.Add("run L1: the log line says $camN camera events, the file holds $($camEvents.Count)") }
        if ($camEvents.Count -gt 0) {
            $yawTick = [int]$camEvents[0].tick
            if ($yawTick -lt $YawAt) { $failures.Add("run L1: the first camera event is at tick $yawTick, before the scripted yaw at $YawAt") }
        }
        if (@($lEvents | Where-Object { $_.key -eq 'W' -and $_.pressed }).Count -ne 1) { $failures.Add('run L1: the input log does not hold the replayed W press') }
        if (-not (Test-Path -LiteralPath $yawExamplePath)) {
            $failures.Add("run L1: no committed example at $yawExamplePath")
        } elseif ([Convert]::ToBase64String([IO.File]::ReadAllBytes($lRecPath)) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($yawExamplePath))) {
            $failures.Add("run L1: the recorded log differs from the committed example $yawExamplePath (regenerate it from run L1 with a stated reason)")
        }
    }
}
$w1 = Get-WardenTicks $l1LogPath
$turnL1 = $null
if ($yawTick -ge 0) {
    if (-not $w1.ContainsKey($yawTick) -or -not $w1.ContainsKey($yawTick + 20)) {
        $failures.Add("run L1: the run log has no TICK $yawTick and TICK $($yawTick + 20) warden lines around the yaw event")
    } else {
        $x0 = [int]($w1[$yawTick] -replace '^TICK \d+ warden=\((-?\d+),.*$', '$1')
        $x20 = [int]($w1[$yawTick + 20] -replace '^TICK \d+ warden=\((-?\d+),.*$', '$1')
        $turnL1 = "$($w1[$yawTick]) -> $($w1[$yawTick + 20])"
        if ($x20 -ge ($x0 - 100)) { $failures.Add("run L1: the Warden's x went $x0 -> $x20 in the 20 ticks after the yaw (W did not walk along the turned camera)") }
    }
}

$ErrorActionPreference = 'Continue'
$l2Text = & $wrapper --headless --path (Join-Path $repo 'game') --fixed-fps 60 --quit-after $YawFrames '--' --replay-input $lRecPath --log $l2LogPath 2>&1 | Out-String
$l2Code = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$l2Lines = @($l2Text -split "`r?`n")
if ($l2Code -ne 0) { $failures.Add("run L2: godot exited $l2Code (expected 0)") }
$scriptedL2 = First $l2Lines '^INPUT (enter|ping|yaw) '
if ($scriptedL2) { $failures.Add("run L2: a scripted press ran during the replay: '$scriptedL2'") }
$camReplayed = @($l2Lines | Where-Object { $_ -match '^INPUT replay cam tick=\d+ yaw=\S+$' })
$replayDoneL = First $l2Lines '^INPUT replay done events=\d+ last=-?\d+ cam=\d+$'
if ($camEvents.Count -gt 0) {
    if ($camReplayed.Count -ne $camEvents.Count) {
        $failures.Add("run L2: $($camReplayed.Count) 'INPUT replay cam' lines for $($camEvents.Count) recorded camera events")
    } else {
        for ($i = 0; $i -lt $camEvents.Count; $i++) {
            $null = $camReplayed[$i] -match '^INPUT replay cam tick=(\d+) yaw=(\S+)$'
            if ([int]$Matches[1] -ne [int]$camEvents[$i].tick -or [double]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture) -ne [double]$camEvents[$i].yaw) {
                $failures.Add("run L2: camera replay line $i is '$($camReplayed[$i])' (want tick=$($camEvents[$i].tick) yaw=$($camEvents[$i].yaw))"); break
            }
        }
    }
    $wantDoneL = "INPUT replay done events=$($lEvents.Count) last=$($lEvents[$lEvents.Count - 1].tick) cam=$($camEvents.Count)"
    if ($replayDoneL -ne $wantDoneL) { $failures.Add("run L2: replay summary '$replayDoneL' (want '$wantDoneL')") }
}
$w2 = Get-WardenTicks $l2LogPath
$comparedL = 0
if ($yawTick -ge 0) {
    foreach ($t in @($w1.Keys | Where-Object { $_ -ge $yawTick } | Sort-Object)) {
        if (-not $w2.ContainsKey($t)) { $failures.Add("run L2: the replayed run log has no TICK $t warden line (L1: '$($w1[$t])')"); break }
        if ($w2[$t] -ne $w1[$t]) { $failures.Add("run L2: '$($w2[$t])' but L1 had '$($w1[$t])' (the replayed yaw is not the recorded one)"); break }
        $comparedL++
    }
    if ($comparedL -lt 20) { $failures.Add("run L2: only $comparedL TICK lines at or after the yaw event's tick $yawTick were compared (want >= 20)") }
}
foreach ($p in @($walkPath, $lRecPath, $l1LogPath, $l2LogPath)) { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p } }

Write-Output '--- asserted HUD lines ---'
foreach ($l in @($summary, $party, $death, $downed, $objective, $slackFirst, $slackLower, $runEnd) + $choices + $causes + @($objB, $ping, $runEndB)) {
    if ($l) { Write-Output $l }
}
Write-Output "--- run-end titles, lane 3C-d (lines=$($titleLines.Count) cleared=$clearedTitles) ---"
foreach ($l in $titleLines) { Write-Output $l }
Write-Output '--- run N (range tint after a non-clear run end), lane TINT-RUNOVER ---'
foreach ($l in @($tintLive, $tintEnd) + $tintOver) {
    if ($l) { Write-Output $l }
}
Write-Output '--- run C (bot party) ---'
foreach ($l in $seats + $botCasts + @($pull2, $tick295, $livePing, $bossC, $lockC, $runEndEv, $runEndC, $botHit) + $causesC + @($botsC, $botsC2)) {
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
Write-Output '--- run L (camera yaw record -> replay), lane 3E-c ---'
foreach ($l in @($yawL1, $recDoneL, $turnL1) + $camReplayed + @($replayDoneL, "compared=$comparedL")) {
    if ($l) { Write-Output $l }
}
Write-Output '--- run M (recorded start heading), lane HOST-HEADING ---'
foreach ($l in $mShown) {
    if ($l) { Write-Output $l }
}

if ($failures.Count -gt 0) {
    Write-Output "HudSceneTest: FAIL ($($failures.Count))"
    $failures | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
Write-Output "HudSceneTest: PASS ticks=$Ticks pingTicks=$PingTicks partyTicks=$PartyTicks bossTicks=$BossTicks queueTicks=$QueueTicks backfillTicks=$BackfillTicks abandonSeat=$AbandonSeat wipeFrames=$WipeFrames pingAt=$PingAt pingFrames=$PingFrames recordFrames=$RecordFrames recordEvents=$recN yawAt=$YawAt yawTo=$YawTo yawFrames=$YawFrames yawCompared=$comparedL"
exit 0
} finally {
    Exit-KeyrunLaunchLock $launchLock
}
