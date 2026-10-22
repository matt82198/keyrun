# HUD scene tests, one file per run

These files replace the former `game/src/Host/HudSceneTest.ps1`. That file held all 14 HUD runs (A to N) and their 41
checks in one script, so every HUD lane edited the same file. Module-boundary review slice S3 split it into one file per
run, plus one file for the source checks. Each file boots its own launches, writes its scene log, and prints
`<Name>: PASS` or `<Name>: FAIL (n)` with the same failure lines as before. Exit codes are unchanged: 0 = PASS,
1 = FAIL, 2 = could not run (Godot missing, or another launch holds the lock; see tools/godot/godot.ps1).

CI (`.github/workflows/game.yml`, job game-e2e) globs `game/src/**/*SceneTest.ps1` and requires `<Name>: PASS` from
each file, where `<Name>` is the file's base name. A new scene test is wired into CI by adding the file; no workflow
edit is needed.

| Run | File | Checks (numbers from the header below) | Launches |
|---|---|---|---|
| A | `HudRunA-SceneTest.ps1` | 1-6, 40, COMBO-BLAME on A, run-end titles on A | 1 |
| B | `HudRunB-SceneTest.ps1` | 7, 8, run-end titles on B | 1 |
| C | `HudRunC-SceneTest.ps1` | 9-13, 13a-13d, HOLDER-LOCK, COMBO-BLAME on C, run-end titles on C and its repeat | 2 (same seed) |
| D | `HudRunD-SceneTest.ps1` | 14-16, run-end titles on D | 1 |
| E | `HudRunE-SceneTest.ps1` | 17-20, run-end titles on E | 1 |
| F | `HudRunF-SceneTest.ps1` | 21-23, run-end titles on F | 1 |
| G | `HudRunG-SceneTest.ps1` | 24, run-end titles on G | 1 |
| H | `HudRunH-SceneTest.ps1` | 27 | 1 |
| I | `HudRunI-SceneTest.ps1` | 28 | 1 |
| J | `HudRunJ-SceneTest.ps1` | 29 | 1 |
| K | `HudRunK-SceneTest.ps1` | 30-32 | 2 (K1 records, K2 replays) |
| L | `HudRunL-SceneTest.ps1` | 33-35 | 2 (L1 records, L2 replays) |
| M | `HudRunM-SceneTest.ps1` | 36-39 | 4 (M1-M4) |
| N | `HudRunN-SceneTest.ps1` | 41 | 1 |
| source | `HudSourceChecks-SceneTest.ps1` | 25, 26 (reads source files, no launch) | 0 |
| tabs | `ScreenTabs-SceneTest.ps1` | S1-S6 (SCREEN-TABS, f-08279d82: opens on Character, tab walk, sheet, seats, Queue starts the run; added after the split, so not part of the 193 count below) | 1 |

`HudSceneCommon.ps1` holds the helpers the runs share: `First`, `FirstRun`, `Assert-BossArmed`, the input-log header
readers, the run-end title checks, the COMBO-BLAME check, the scene log writer and the PASS/FAIL printer. Runs F and G
share `HudBackfillCommon.ps1` (the `--abandon-seat` parse and `Invoke-Backfill`). Neither helper matches the CI glob,
and neither launches Godot. Each run file defines its own launch function (`& $wrapper`) and holds its own launch lock,
so `tools/godot/launch-lock-test.ps1` case G parses every launch and checks its lock.

## Assertion count

The split is mechanical: no assertion was added, removed or loosened, and each assertion's text and regex are
byte-for-byte the monolith's. The monolith held 193 `failures.Add` calls. The files in this folder hold 193: each
shared check lives once, in a helper.

Two checks spanned runs in the monolith and are now applied to each run's own lines:
- **Run-end titles (lane 3C-d).** These used to read the `HUD runend result=` lines of runs A, B, C (twice), D, E, F and
  G together. Each of those files now checks its own lines with the same rules. The guard "no `HUD runend result=` line
  in any autorun" is checked on run A, which always prints one.
- **COMBO-BLAME.** This used to loop over runs A and C. Each of those two files now checks its own run.

The monolith's `-HeadingOnly` switch ran the source checks and run M alone. The same pair is now
`HudSourceChecks-SceneTest.ps1` and `HudRunM-SceneTest.ps1`.

## Header of the former HudSceneTest.ps1 (verbatim)

The check numbers above refer to this text. Tick budgets and their history live here, and each run file keeps its
budget as a parameter with the same default.

```text
.SYNOPSIS
  Headless scene test for the 3C HUD (ENGINE-PLAN phase 3, lane 3C; BIBLE s7 choice of three, s8 Slack, s10 pings):
  party frames, objective line, Slack bar, SPREAD/STACK labels and the run-end panel on the real run root.

.DESCRIPTION
  Boots the real game (Main.tscn -> Arena.tscn) headless at --fixed-fps 60 twice and reads the HUD's own
  "HUD ..." lines from the engine output (the HUD prints what it draws when --autorun is on):

    Run A (pull 1, the Choir, solo Warden):  -- --autorun --ticks 10000 --solo
      (10000 since keyrun #446, was 900: R-BACKFILL-2 (BIBLE s9.1, f-47a7a6db; the #446 lane brief's R-WIPE-RESET) makes
      a wipe on a layout run a release and a run-back, not a run end. The solo Warden dies at 362 and respawns at the
      entrance at 463 (RunWiring.ReleaseDelayTicks after the death step), the 900-tick run printed no "HUD runend" line
      ("AUTORUN done ticks=900 ... run=running" on #446's game-e2e), and the run ends only at the timer cap
      (Timer.CapTicks, twice par on the run clock) at 9600, depleted and uncleared. 10000 keeps the 294 ticks past the
      end the budget kept before; tools/Keyrun.Playtest.Tests/HudSceneWipeReleaseRunsTests.cs pins the end inside it)
      (900 since PR #362, was 600: a Warden with 3500 bp armor dies at tick 505 with run_end at 606 by measurement)
      (600 since lane NAV-CORE, was 400: the Warden first walks from the Entrance into the Choir room, so the wipe's
      run_end moved from tick 374 to 413)
      1. exit 0 and "AUTORUN done ticks=900";
      2. "HUD party seat=1 name=Warden hp=H/400 res=R/M" (the party frame reads the human's health and resource);
      3. "HUD party seat=1 ... DOWNED" after the sim's death event for actor 1 (the DOWNED chip), and every party frame
         from it to his release is DOWNED; R-BACKFILL-2: his 'EVENT {"actorId":1,"tick":T,"type":"respawn"}' comes at
         T = death tick + 1 + 100 (RunWiring.ReleaseDelayTicks), and the first party frame after it is not DOWNED and
         reads hp=400/400 (a released seat respawns at full health, Release.cs);
      4. "HUD objective pull=1/4 id=choir name="The Choir"" (the current pull's content name);
      5. "HUD slack ticks=620 of=620" first (Timer.InitialSlackTicks(2) = 600 + 20), then a "penalty=N" above 0
         after the death (STATE.md D46, Mythic+: a death adds 300 to the run clock and the wipe 500 more), with the
         bank never drained below 620 (deaths no longer spend it);
      6. "HUD runend result=depleted choices=3 cleared=0 ... title="Out of time -- key stays at +2"" (R-BACKFILL-2: the
         run ends at the timer cap, depleted and uncleared; the panel must not call it cleared, and RunEndTitle.ForRun
         titles an uncleared depleted layout run "Out of time"; was "Party wiped on pull 1 of 4 -- key stays at +2"
         while a lasting wipe ended the run, lane 3C-d), three "HUD runend choice n=<1..3> slot=<slot>
         tier=<t> facet=... ability=warden.<id> charges=<c> id=reward-<slot>-warden.<slot>.<facet>-t<t>-<i>" lines
         (Rewards.RollRewardChoices over the Warden's content facet pool, lane 3C-d: no placeholder "ability-<n>"
         id), "HUD runend key from=2 to=2 tier=0" (an untimed key holds its level, KeyMove), and "HUD runend cause
         seat=1" lines naming only seat 1's own causes; every "HUD runend result=" line of
         every autorun (A-G) titles a cleared=1 run "Run cleared -- ..." and a cleared=0 depleted run "Out of time --
         key stays at +<key>" (R-BACKFILL-2: every host run is a layout run, so a wipe never ends one and no title says
         "Party wiped"; was "Party wiped on pull <n> of <total> -- key stays at +<key>") (an untimed key keeps its
         level: Matt 2026-10-20, f-7c7abdf4; "key depleted" is banned from every title) (run C clears the dungeon since
         lane TUNE-BOSS, so the clear title is checked on it; the count of cleared runs is printed).
    Run B (from pull 2, the Pair, solo Warden):  -- --autorun --ticks 900 --pull 2 --solo
      (600 since lane NAV-CORE, was 400: the Pair now spawns at the far side of its hall, 7 m off, run_end at tick 455)
      7. "HUD objective pull=2/4 id=pair name="The Pair"" (a later --pull start still numbers the pull in the full
         dungeon), and R-BACKFILL-2 (BIBLE s9.1, f-47a7a6db; the #446 lane brief's R-WIPE-RESET: a wipe is a release and
         a run-back, not a run end): the solo wipe does not end run B, so no "HUD runend result=" line in its 900 ticks
         (it ends only at the boss kill or the timer cap, past 8000 ticks), and the dead Warden's 'EVENT {"actorId":1,
         "tick":T,"type":"respawn"}' comes at T = death tick + 1 + 100 (RunWiring.ReleaseDelayTicks; HudSceneRunsBKTests).
         Was the wipe title "Party wiped on pull 2 of 4 -- key stays at +2", which no layout run can print now;
      8. R-PILE-10 (dungeon loop decided as WoW Mythic+ does; BIBLE s3 cast bars and telegraphs; wrong data shown is
         P0): a mechanic aimed at a living player is legitimate and drops when he dies. From run B's log:
         (a) the Warden's death tick is the "tick" of the first 'EVENT {"actorId":1,...,"type":"death"}' line;
         (b) every "HUD ping label=SPREAD|STACK" line is printed in a step (the latest "TICK n" line above it) strictly
             before that tick. Re-pointed 2026-10-21 (ruling: gates keep their purpose, scenarios move): the Pair's caster
             telegraphs are gone, so a call exists only once a mark LANDS on a player (PingCalls.cs, mark source), and no
             Pair mark lands in run B before the Warden dies (the mark completes after tick 354). The "a call before the
             death" proof therefore moved to the runs where a mark lands: run C (check 11) and E2eAssert's bot run
             (E2eAssert.ps1 line 165). Run B keeps the rest of the rule: no call at or after his death (d);
         (c) no Pair caster telegraph is drawn at ANY tick of run B (a "telegraph" EVENT whose cast is pair.*: the ring on
             the marked player is the telegraph, BIBLE s3, s9), no telegraph is drawn after the death, and on the death
             tick every live telegraph cancels (TELEGRAPH-CANCEL-ON-DEATH): the 'EVENT {... "type":"telegraph_cancel"}'
             lines with that tick number exactly the telegraphs live then (the "telegraph" EVENTs up to it, less the
             "telegraph_hit" EVENTs up to it and the cancels before it). With none live at tick 354 (the case now) that
             is zero cancels; the death tick's own 'TICK n' line must be in the log, so the check never passes on an
             empty run;
         (d) after the death EVENT no "CAST start ... target=1" line (R-DEADCAST-1: no cast starts on a dead target;
             every CAST start line names its target) and no further "HUD ping" line while he is dead: R-BACKFILL-2, up to
             and including the step of his respawn EVENT (as HudSceneRunsBKTests walks it); once he has respawned he is a
             living target again and the Pair's calls on him are legitimate. (b) reads the same window.
         Was (SLAM-TELEGRAPH): NO ping at all, on the premise that the solo Warden is down before the Pair's first
         mark (about tick 295). PR #362's Warden armor (3500 bp) keeps him alive to tick 354, so the call at 295 is an
         honest live-run call; the scenario is not changed to make him die sooner (R-PILE-10). Run C (check 11) also
         proves the live-run call.
    Run C (pull 1, the three-seat bot party, lane 3H), twice with the same seed:
        -- --autorun --ticks <PartyTicks> --careless-seat <CarelessSeat>
        (default 13000 since f-3dcc702c: pull to 100% forces puts the boss at tick 9746 and run_end at 12294 at key 2 seed 1;
        9000 since R-DOOR-1, was 7000; the header said 3700, the value before lane TUNE-BOSS made the run reach the boss)
        (--careless-seat S@T:GROUP, CARELESS-SEAT, default 3@300:pair; Matt 2026-10-20, verbatim: "no a bad player will
        accidentally aggro more groups, just test it naturally", STATE.md D43: under PRESPAWN-AGGRO the Warden walks
        into every ring first, so no enemy ever picked a bot seat. Bot seat S (the Cinder) plays carelessly from run tick
        T, mid-Choir: it strays off the party toward the idle Pair, waits at the Choir room's far doorway while the Choir
        fight lasts (Careless.HoldPoint; no gate walls it since STATE.md D44, PATH-TO-BOSS, so it pulls between fights, not
        mid-fight), runs on when the Choir clears, and walks into the Pair's aggro ring ahead of the Warden. The sim's own aggro rule engages
        the Pair on it and the Pair's own targeting (no threat yet, so the nearest living player) goes for it. Nothing
        makes an enemy hit a bot; check 12's bot-damage line comes from that pull.)
      9. "HUD party seat=1 name=Warden", "seat=2 name=Chanter", "seat=3 name=Cinder" (BotCommandSource drives 2, 3);
     10. in pull 1 (before the Pair spawns): a Chanter heal on a party seat and a Cinder damage hit on an enemy (at
         seed 1 the 3B healer role spends every GCD on heals and never casts Dissonance; the Cinder kit has no heal);
     11. the party reaches the Pair or run_end, is not wiped by tick 295, and a "HUD ping label=SPREAD|STACK" line
         appears before the party's third death (a call during a live run), or (R-WARDEN-FILLER-1, keyrun #540) a
         Pair mark cast (pair.spreadMark or pair.stackCall) is interrupted before it, since the filler Warden's Sundered
         lets the Cinder's Shatter stun the last Paired Warrior mid-cast; the live call stays required by E2eAssert.ps1;
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
    Run E (lane 3I, the queued run; solo Warden, as ScreenSceneTest):  -- --autorun --ticks 10000 --solo --requeue
        (10000 since keyrun #446, was 900: its first run is run A's, which under R-BACKFILL-2 no longer ends on the solo
        wipe but at the timer cap at 9600, so the screen can queue only after that; 10000 keeps the 294 ticks past the
        end that the queued run's Choir spawn needs, pinned by HudSceneWipeReleaseRunsTests)
        (--requeue: an autorun starts the queued run only when asked, so runs A-D and the other scene tests stay one
        run each; an interactive game always starts it; 480 -> 600 by ROOM-LOCK-IN D27: the solo Warden then died at 390
        and the depleted run_end landed at 491, past the old 480; 600 -> 700 by SWING-PHASE (R-307-E): the Choir's
        auto-attacks now swing on their own phases, so the solo Warden dies at 427 and the depleted run_end lands at
        528; --ticks counts both runs, and the requeued run needs about 75 ticks to engage the Choir ("PULL spawn
        index=0") plus the screen's key script, so 528 + 75 + screen is about 608, past the old 600; 700 keeps D27's
        margin shape; 600 -> 900 by PR #362 on its own parent (a 3500 bp Warden dies at 505, run_end 606); the
        integration merge keeps 900, the larger of the two raised budgets, not re-measured on the integrated tree)
     17. the first "RUN start" says "key=2 clause=none" (no queue yet: today's run);
     18. "SCREEN queue dungeon=hollow-vault key=3 clause=" (the screen's autorun script queues key 3; R1: below run.json
         clauseStartKey 5 the key carries no Clause);
     19. after it, a second "RUN start ... key=3 clause=none" (the key is the run content's KeyLevel and the
         Clause is the sim's EncounterRules.ActiveClauseId, so a request that is dropped on the way reads key=2 or
         clause=none), followed by a second "BOSS armed" and "PULL spawn index=0 id=choir";
     20. then "QUEUE consume n=1 size=0" (lane 3J: the request that started the run leaves the Queue node, so it can
         never also refill a seat).
    Run F (lane 3J, backfill in the real loop; three-seat party, key 2, no --requeue):
        -- --autorun --ticks 3500 --abandon-seat <AbandonSeat> --joiner-key 2
        (3500 since PARTY-TOGETHER, was 3300. Since JOINER-ARRIVES (BIBLE s9.1/s10 "a joiner arrives at the next pull
        start ... inside the party's taken branch") the refilled seat is placed at the pull start on the nearest living
        staying party member and casts from its first tick there: on seed 1 key 2 the Chanter refills at 2052 on the
        Cinder and completes chanter.chorus at 2053 (headless replica RunFJoinerActsTests))
        (--abandon-seat A@T: SimDriver calls AbandonSeat(A) at run tick T, or A@pullN+O (R-415-D) O ticks after pull
        N's pull_start, logged "SEAT abandon actor=A tick=T pull=N start=S offset=O" with T = S + O, or A@pullN.hpP+O
        (f-0fd3e6fb) O ticks after the first step that ends with pull N's standing enemies at or below P percent of their
        health, logged "SEAT abandon actor=A tick=T pull=N hp=P at=S offset=O" with T = S + O; with --joiner-key K, it adds one
        QueueRequest at key K straight to the Queue node at Hud/Screen/Queue; this is the scripted path, the screen's
        Queue button is not pressed mid-run)
     21. "SEAT abandon actor=A tick=T" (pull-relative: "... pull=N start=S offset=O", health-relative: "... pull=N hp=P
         at=S offset=O", T = S + O) in pull 1, then
         "QUEUE add dungeon=hollow-vault key=2";
     22. the party survives to "PULL spawn index=1 id=pair", and in that same step (no TICK line between them)
         "SEAT refill actor=A tick=X key=2" with X = that step's TICK tick + 1 (Backfill.Step's FillTick: the first
         tick whose commands are the joiner's), then "QUEUE consume n=1 size=0";
     23. the open seat stands idle: no cast_complete EVENT with "actorId":A between the abandon and the refill, the
         refilled seat casts again after it (the joiner's commands run from the pull start), and no party seat dies
         before pull 2. Default A@T = 2@1500 since lane WARDEN-ENGAGES (the Chanter leaves at tick 1500, late in the
         Choir fight; the Warden now fights the Choir melee after the kick, so seed 1 key 2 clears the Choir at 1757 and
         pull 2 starts at tick 1840 with all three seats alive; 2@3000 from lane NAV-CORE now falls in pull 2; 2@1500 -> 2@1700
         by ROOM-LOCK-IN D27 (Choir pull_start 50, room locked): a Chanter leaving at 1500 lets the Warden die at 1794, before
         the 1809 clear; at 1700 the Choir clears at 1749, pull 2 starts at 1844 and the seat refills at 1844; 2@1700 -> 2@pull0+1350 by
         R-415-D, a leaver mid-pull is a scenario, not a number: on #415's gear-free pacing the Choir clears at 1532, so
         1700 fell 55 ticks into the Pair and the party wiped with no pull start to refill at; the Choir's pull_start is 76
         on main and on #415, so 2@pull0+1350 leaves at 1426 on both, inside the Choir: with the bot gear this host runs
         (RunFJoinerActsTests) it clears at 1969 and the seat refills at 2082 on main and on #415; gear-free it clears at
         1969 on main and 1549 on #415, refill 2082 and 1662). #362 had separately moved 2@1700 -> 2@1500
         (D-PINNED-FIXTURES-362: with the healer press the undisturbed Choir clears at 1580 and pull 2 starts at 1693, so
         1700 fell in pull 2); the merge-forward onto #415 keeps the pull-relative form, which serves that same intent
         (the leave stays inside the Choir), and RunFJoinerActsTests pins the leave inside pull 0 on the merged tree.
         BOTS-FIVE-BUTTONS (#298) had likewise moved 2@1700 -> 2@1500 (its bots clear the Choir at 1552); the bots
         integration keeps main's anchored form for the same intent.
         2@pull0+1350 -> 2@pull0.hp10+1 by f-0fd3e6fb: a fixed offset from the Choir's engage still flipped whenever the
         Choir's fight length changed (the setup lesson, #364 and #326 each moved it), so the leave now waits for the
         Choir's own progress, one tick after its standing enemies are down to a tenth of their health: on seed 1 key 2
         that is 1458 (Choir clear 1609, refill 1722). Measured headless (RunFJoinerActsTests, bot gear): the party
         reaches the Pair with the leave at 20% (1343) or below and wipes at 25% (1308, the Warden dies at 1649); a leave
         one tick after the Choir's first death (1031) wipes as well (the Warden dies at 1454). RunFAbandonPacingTests
         moves the Choir's engage and fight length and shows 2@pull0.hp10+1 stays inside it where 2@1500 and
         2@pull0+1350 do not.
         The old 1@1000 cannot work on the
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
        (<WipeFrames> 41200 since keyrun #446, was 6600: under R-BACKFILL-2 the dead seats release and respawn, so the
        idle party's run ends only at the timer cap, depleted, at tick 12115 (HudSceneWipeReleaseRunsTests, Clause echo as
        game-e2e's "RUN start" reads); 3 frames per tick of that plus the 4794 frames the budget kept around the old
        run_end at 602 for the screen, the presses and the queued restart)
        (--press-enter N: SimDriver's scripted player pushes Enter through the viewport each time the game waits on
        it, the screen up or the run ended, up to N presses; the real keyboard path, not a direct call)
     28. "SCREEN first open=1 key=2", press 1 queues the first run ("RUN start ... key=2 clause=none": R1, key 2 is
         below run.json clauseStartKey 5), the party
         wipes ("run_end" EVENT, result depleted), press 2 on the run-end panel prints "SCREEN reopen
         result=depleted open=1 key=2" and NO "RUN start" comes between the run_end and that line, nor between it and
         press 3; press 3 is the screen's Queue, and the next "RUN start" is its key-2 request (key=2 clause=none, R1;
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
     predates the field and fixtures are not regenerated); K1 asserts that line separately. K1's and L1's comparisons
     also leave out the "rotationIndex" header line (the weekly affix rotation, AffixWeek.cs): K1 asserts it is there,
     L1 that it is 0 (L1 replays a log without one, so its run is built at 0, never at today's week).
    Run A also carries the ability bar icons (lane ABILITY-ICONS; the bar prints one line per slot under --autorun):
     40. nine "HUD bar slot=<key> ability=warden.<id> icon=res://assets/kenney-board-game-icons/<file>.png tex=64x64"
         lines, one per slot 1-5, Q, E, R, F (tex is the loaded Texture2D's size, so a missing file or an unmapped id
         reads icon=none tex=none and fails). Before ABILITY-ICONS the bar draws only text: no "HUD bar" line, 40 RED.
    Run N (lane TINT-RUNOVER, a non-clear run end with a living out-of-range target; three-seat party, key 2):
        -- --autorun --ticks <TintTicks> --abandon-seat <TintAbandon>   (default 8500 and 2@300, no --joiner-key)
        (Re-derived for the #449 bots, lane fix/run-n-wipe-scenario-449: at e87cade0 the old 2@200 let the party clear
        all four pulls (last clear at 8231, cap end 8232, PullIndex 4/4), so the cap end was not uncleared. 2@300 wipes
        from tick 596 on (both living seats down at 596) and ends at the timer cap uncleared at tick 7643, measured
        headless by HudSceneWipeReleaseRunsTests. The Godot-side bar ticks below are from before #449 and are NOT
        re-measured in game; game-e2e is the arbiter of check 41 for 2@300.)
        (the bar prints "HUD bar range tinted=N over=0|1 tick=T" under --autorun whenever the count of range-tinted
        slots or the run-over state changes. At seed 1 the Chanter leaves at 200, still at the entrance (14771,-998),
        the Warden dies at 427 where he stands (19246,-405), the Cinder at 583, and the Choir enemy the Warden
        targeted (100) walks on to the Chanter, 3216 mm from the Warden's body, past his 3000 mm reach: the bar tints 4
        slots at 585, and the party wipes: run_end depleted at 840.
        Why 2@200, not 2@600 (PRESPAWN-AGGRO, PR #308): the Choir now engages at its aggro ring (pull_start 76) and
        not on room entry, so by 600 the Chanter has walked up into the fight and the whole party stands within about
        3.7 m of the Warden. With 2@600 he died at 895 and his target stayed within 1855 mm of his body (inside his
        reach) until the wipe at 1308, so the live tint never showed and the control could not happen (CI run
        39486265558, "HUD bar range tinted=0 over=1 tick=1308" its only range line). The assertion is unchanged; only
        the leave tick moves, so the party still wipes with the Warden's target alive and out of range.)
        (<TintTicks> 8500 since keyrun #446, was 1400: under R-BACKFILL-2 the wipe releases the party instead of ending
        the run, so the non-clear end is the timer cap's, depleted and uncleared at tick 7643 under 2@300 (8186 under the pre-#449 2@200 wipe), pinned with 294 ticks past
        it by HudSceneWipeReleaseRunsTests. Measured headless on the current bots (#449; seed 1, key 2, 2@300): the cap
        end at 7643 freezes the Warden's target at 102, alive, 2289 mm off, inside his 3000 mm reach, from that end to the
        budget. Seeds 1-20 give identical runs, and no post-cap tint window exists in the scanned space (abandon ticks
        50-2000, keys 1-5), so the "over=1 reads tinted=0" lines here hold by construction, not by the switch. The switch is
        covered headless by HudRunOverTintTests (ruling f-c9a92309, option c): a living hostile target beyond reach tints
        with runOver off and does not tint with it on. The older 105 at 2166 mm note is stale.)
     41. a "HUD runend result=depleted ... cleared=0" line (the run did not clear), at least one "HUD bar range
         tinted=N over=0" line with N > 0 (control: the live run tints), at least one "over=1" line, and every
         "over=1" line reads tinted=0. Before TINT-RUNOVER the bar passed no runOver and printed "tinted=4 over=1
         tick=1271" (the 2@600 run before PRESPAWN-AGGRO): 41 RED.

  Run from the repo root:  powershell -ExecutionPolicy Bypass -File game/src/Host/HudSceneTest.ps1
  Exit 0 = PASS, 1 = FAIL, 2 = could not run (Godot missing; see tools/godot/godot.ps1).
```

The last two lines above are historical: run one file now, for example
`powershell -ExecutionPolicy Bypass -File game/src/Host/scenes/HudRunA-SceneTest.ps1`, from the repo root.
