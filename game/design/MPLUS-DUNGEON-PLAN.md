# Mythic+ dungeon plan: the POC dungeon played as Mythic+

PURPOSE: resynthesize the keyrun POC dungeon plan so Hollow Vault plays like WoW Mythic+, as file-disjoint slices
lanes can ship in parallel. Rule 0 inside the dungeon: lean toward WoW unless the bible says otherwise
(design/BIBLE.md:264-269, "Mythic+ wins unless a section 0 test or a recorded Matt decision says otherwise").
Main means origin/main at 3bb6eed5 (2026-10-21). Every "main" claim cites file:line on that tree. Every WoW claim
is written as "WoW does X". This plan replaces the slice order of game/design/FORCES-ROUTE-PLAN.md section (3)
(:417-432) where they differ. Its packs, points and kits (:368-398) still stand.

## Matt's verdicts tonight, each answered by a slice

| # | Verdict (verbatim) | Answered by |
|---|---|---|
| V1 | "theres still no ability animations" | T4 ABILITY-ANIM |
| V2 | "still no pathing which was supposed to be done yesterday, just a linear route... drifted" | T5 SIM-PATHING (pathing), N2 BRANCH-LAYOUT (route) |
| V3 | "The bosses die faster than trash and dont teach anything" | T3 BOSS-TEACHES |
| V4 | "i want to be able to see my party, idk why you made them transparent" | T1 PARTY-SOLID (#541) |
| V5 | "run the playthroughs vertical camera" | T2 CAMERA-VERTICAL |
| V6 | "the boss should be like a minute still on learning dungeons minimum lol, 10second boss is insane" | T3 (R-BOSS-1: 90 to 180 s at key 2) |
| V7 | "wwhat happened to pathing, pulling, 100%, aggro range?????????" | pathing T5; pulling N1 LINKS, N3 LEASH, N4 LOS-PULL, N7 RINGS-VIEW, N8 BOTS-ROUTE; 100% N5 FORCES-COMPLETION; aggro range is on main (gap row 6) and drawn by N7 |
| V8 | "the rooms should not force you to one path, where in the bible does it say linear dungeon?" | N2 BRANCH-LAYOUT (D45) |
| V9 | "We need the timer, the kill count, and everything in the POC dungeon too" | T6 SCOREBOARD-LIVE |
| V10 | "and diversify the packs?" | T10 S2 VAULT-FORCES, then N2 (the roster in section 4) |
| V11 | "do you want to resythensize the plans and steps you should be following?" | this file |
| V12 | "this does not feel like m+ at all, its like vampire survivors with kicks" | the section below, then every slice |

### "Vampire survivors with kicks": what it is on main, and the M+ version

| Survivors trait | Main today (evidence) | The M+ version instead |
|---|---|---|
| Mobs stream at you | Every engaged enemy moves straight at its target every tick (game/sim/Keyrun.Sim/Encounter/Chase.cs:85-87). An actor that hits a wall is only clamped back (game/sim/Keyrun.Sim/Run/Run.cs:358), it never paths around it. Nothing leashes: "leash" appears nowhere in game/ (.cs or .json). | WoW mobs path around walls on a navmesh, and an engaged mob evades home when pulled past its leash. T5 builds integer pathing (R-PATH-1). N3 builds leash and the wipe reset. |
| One arena | The layout is one chain of boxes on one line: entrance, choir-room, pair-hall, seed-chamber, warden-arena (game/content/data/layout.json:4-39), and the route is nine points all at z=0 (:40-50). | WoW dungeons are rooms and corridors with alternative routes. N2 builds the bible 9.1 Fork (design/BIBLE.md:331-336). |
| No positioning | The sim has no line of sight (no LineOfSight or LOS rule in game/sim, game/bots or game/content/Keyrun.Content). A caster casts from wherever it stands. No aggro ring is drawn (no ring view in game/src/View). | WoW tanks LOS-pull ranged mobs around a corner so the pack stacks in melee. N4 builds LOS, N7 draws the rings. |
| No pulling | Packs get no links (game/sim/Keyrun.Sim/Encounter/Aggro.cs:100, `Array.Empty<string>()`). Patrols cannot exist, because GroupsOf throws the tick away (Aggro.cs:86, `_ = tick;`). | WoW chain pulls and patrols are route decisions. N1 builds links, #528 builds patrols. |
| No route | The run ends on the pull index alone (game/sim/Keyrun.Sim/Run/RunWiring.cs:368), so forces decide nothing. The three layout packs are copies of teaching-pull enemies (layout.json:55-81). | WoW needs 100% forces plus the boss, and every pack is a choice. N5 builds completion. T10 and N2 place the bible packs. |
| Kicks are the only skill | Kicks work (Choral Mending on a 200-tick loop, content/data/enemies/choir.json:20). Nothing else asks for a decision. | WoW asks for kicks plus positioning, kill order, route, pull size and cooldowns. Every slice below adds one of them. |

## 1. The M+ loop in one page

Tags: **BIBLE** means the bible decides it. **WoW** means the bible is silent and WoW decides it.

1. **Queue to dungeon in under a minute** (BIBLE s0 test 2, :13-14). One screen, one Queue press, one ready check.
2. **Five buttons plus Q E R F G** (BIBLE s2, :26-35). No consumables, no invisibility, no skip potions (:270-273).
3. **Prespawned packs.** Every group stands idle from tick 0 and engages on its aggro radius or on damage. There are no gates (BIBLE :338-345, D44).
4. **Rooms branch, and the player chooses the route** (D45, BIBLE 9.1 :331-336). The route is forward-only with no
   backtracking, but it is never linear. WoW lets any route reach any room, and has no seal, so no seal is built (D44 forbids gates).
5. **Packs of 3 to 5 with distinct roles** (WoW). A must-kick caster, a bruiser with a frontal, a healer, a ranged mob that must be LOS-pulled, an enrager, and a patrol that joins mid-fight. The bible's 9.2 packs already have this shape (:422-426, section 4).
6. **6 to 8 trash types, each with one or two telegraphed abilities** (WoW). Every dangerous cast has a bar and a floor telegraph, shape plus colour (BIBLE s3 :45-46).
7. **Aggro by radius with a leash** (WoW). The tank pulls by entering the ring or by attacking. An engaged mob pulled past its leash from home evades back at full health. On a wipe, every engaged living mob resets home (WoW). Dead mobs stay dead (BIBLE :402-404).
8. **Chain pulls and LOS pulls are route decisions** (WoW, BIBLE :360-362, :382-385). A link is a dashed line between rings.
9. **Patrols walk fixed loops** (BIBLE :346-348). WoW patrols wander into a fight if the party pulls near the patrol's path.
10. **Forces** (BIBLE 9.2 :410-414). Every mob has a point value, 1000 points = 100%. The run completes only with the boss dead **and** forces at 100% (WoW, R-PACKS-3). Overflow is wasted (WoW, R-PACKS-6).
11. **The timer** (BIBLE s8 :228-243). Par counts from tick 1. Each death costs 15 s (BIBLE :240-243, D46). The key result is +1 under par, +2 under 80%, +3 under 60% (BIBLE :251-253). An untimed key keeps its level (BIBLE :254-255; WoW depletes it, and the bible wins for test 3). A wipe is a release and a run back (BIBLE :398-404).
12. **Bosses** (BIBLE 9.6 :521-530 names the mechanics; WoW sets the shape). WoW cycles three to four mechanics on a timer, and a low-key boss lasts 90 s or more. R-BOSS-1: 90 to 180 s at key 2, with Cleave, Twins and Lock each at least twice, each with a bot answer.
13. **Three pulls and a boss that each teach one thing** (BIBLE 9.6 :512-519): Choir the kick, Pair spread and stack, Seed the Window rule, then the Warden.
14. **Synergy windows** (BIBLE s4 :88-108). The Lock is a combo gate.
15. **Affixes** (BIBLE s8 :207-227). From +5, Fortified or Tyrannical rotates weekly. From +10, both are on. Clauses layer on top. The week rolls at the WoW US reset (game/src/Host/AffixWeek.cs:1-10). Both stay as they are.
16. **Social** (BIBLE s0 test 3, s10). No shareable score, backfill instead of abandonment, pings instead of chat, and cast bars as the enemy's language.

## 2. The gap table

Status values: done, in lane (worked tonight, no branch on origin), PR open, blocked, not started. PR state is
from `gh pr list --repo matt82198/keyrun --state open` at 2026-10-21 23:58Z.

| # | Feature | Main today (file:line) | What M+ needs | Status | Slice |
|---|---|---|---|---|---|
| 1 | Ability animations | Body clips only: death, cast loop, swing one-shot, hit (game/src/View/ActorAnimator.cs:10-25). No particle, projectile or VFX node anywhere in game/src or game/scenes. | WoW shows every ability as a cast visual plus a travel or impact effect, so a press reads on screen | in lane | T4 |
| 2 | Party drawn solid | An occluding ally fades to 250 permille (game/content/Keyrun.Content/Model/PartyFade.cs:46). Bodies near the camera fade too (game/src/View/ActorView.cs:22-28). | WoW keeps party models solid by default. Matt: "see my party" | PR open #541 (BLOCKED) | T1 |
| 3 | Playthrough camera | Third-person boom at 5000 mm and -300 mrad pitch (game/content/Keyrun.Content/Model/CameraFraming.cs:18, :30) | A top-down capture camera for playthrough recordings, so route, rings and pulls read in one frame | in lane | T2 |
| 4 | Boss length and lessons | The boss has 5000 health (content/data/enemies/hollow-warden.json:5) against 6000 for each Choir trash mob (content/data/enemies/choir.json:5, :16). Slam, Cleave and Lock loop every 300, 160 and 600 ticks (hollow-warden.json:9-14). | 90 to 180 s at key 2, with every mechanic shown twice or more (R-BOSS-1). WoW low-key bosses last that long | in lane | T3 |
| 5 | Pathing | Chase moves straight at the target (Chase.cs:85-87). Walls only clamp (Run.cs:358). Bots walk straight at route points (game/bots/Keyrun.Bots/shared/Follow.cs:97-98). No Godot navigation exists (no NavigationAgent or NavigationRegion in game/), which is correct under R-PATH-1. | Deterministic integer pathing in the sim around walls and through doorways (R-PATH-1) | in lane | T5 |
| 6 | Aggro radius | Done. A group engages whole on a party member inside its radius, or on damage (Aggro.cs:23-26). The radius comes from content (layout.json:37, :58, :66, :75). The Seed is room-activated (Aggro.cs:37). | Same as WoW | done | (none; N7 draws it) |
| 7 | Leash and wipe reset | None ("leash" is absent from game/). Engaged mobs chase forever (Chase.cs:51-89). | WoW: an engaged mob pulled past its leash from home evades home at full health, and every engaged living mob resets on a wipe | not started | N3 |
| 8 | Chain pulls (links) | Packs get no links (Aggro.cs:100). Bible :360-362 defines them. | A link drawn as a dashed line, so engaging one engages both | not started (after #528) | N1 |
| 9 | LOS pulls | No line-of-sight rule in the sim (row "No positioning" above) | WoW: a ranged mob with no sight line to its target walks until it has one, so the tank breaks LOS to stack the pack | not started | N4 |
| 10 | Patrols | GroupsOf discards the tick (Aggro.cs:86). Bible :346-348 calls for a W1 patrol. | Fixed deterministic loops whose ring travels with them | PR open #528 (BLOCKED, balance red) | T9 |
| 11 | Branching rooms | One chain on z=0 (layout.json:4-50) | The bible 9.1 Fork, West and East, rejoining at the Seed, with no seal (D44, D45) | not started | N2 |
| 12 | Pack diversity | Three layout packs: cantor-vigil (3 Cantor, 4 Acolyte), hall-sentries (5 Sentry), warrior-remnant (5 Warrior, 2 Cantor) (layout.json:55-81). The five bible kits are loaded (game/content/data/enemies/vault.json:2-60, #538) but placed in no pack, and have no forces value (game/content/data/forces.json:3-9). | The bible 9.2 packs W1, W2, E1, E2, E3 (BIBLE :422-426) and the roster in section 4 | in lane (S2), not started (N2) | T10, N2 |
| 13 | 100% forces to complete | The run ends when PullIndex reaches the pull count (RunWiring.cs:368). Forces are a readout only (FORCES-ROUTE-PLAN.md:414-415). | WoW completes a run only with the boss dead and forces at 100% (R-PACKS-3) | not started | N5 |
| 14 | Overflow wasted | Nothing pays past 100% (R-PACKS-6, FORCES-ROUTE-PLAN.md:478-479) | Same as WoW | done | (none) |
| 15 | Timer, par, death cost | Par is 14400 ticks, 12 min (content/data/dungeons/hollow-vault.json:4). Death costs 300 ticks and a wipe 500 (game/sim/Keyrun.Sim/Encounter/Timer.cs:35, :38). The cap is 2x par (Timer.cs:133). The HUD has TimerSlack and ForcesBar nodes (game/scenes/Hud.tscn:42, :52). | A live timer, deaths line and forces count in the POC dungeon run. Matt reports that he does not see them. | in lane | T6 |
| 16 | Key result | +2 at 8000 bp and +3 at 6000 bp of par (Timer.cs:121-124). Timed means on the clock and not depleted (Timer.cs:163-166). The +2 and +3 marks are on the bar (game/src/Hud/TimerSlack.cs:79). | Same as WoW, except that an untimed key keeps its level (BIBLE) | done (sim), view in T6 | T6 |
| 17 | Kill count | "Enemy Forces 16.00%" label (game/src/Hud/ForcesBar.cs:14-15) | Live in the run, with an "x/1000" count | in lane | T6 |
| 18 | Affixes | Affixes.cs (game/sim/Keyrun.Sim/Encounter/Affixes.cs) and the weekly index (AffixWeek.cs:1-10) | Same as BIBLE s8. **Stays unchanged** | done | (none) |
| 19 | Hazard rooms | game/content/data/hazards.json is `[]`, and the sim has no Hazards.cs | Vent Gout and Floodjet (BIBLE 9.4 :490-502) | not started | N6 |
| 20 | Aggro rings drawn | No ring view in game/src/View (ActorAnimator, ActorView, CameraRig, CastBarView, DungeonBuilder, GearVisual, GreyBox, LabelView, LockView, TelegraphView, WindowRingView) | A grey dashed ring at the radius, a dashed link line, and the patrol's path (BIBLE :373-377) | not started | N7 |
| 21 | Bots route and pull on purpose | Bots walk route points and pull whatever stands on the line (Follow.cs:233-234, :274-290) | Pick a pack set that reaches 1000, enter those rings tank-first, and skip the rest (BIBLE :363-364) | not started | N8 |
| 22 | Warden filler rotation | The autorun Warden lives in game/src/Host/ScriptedWarden.cs:23 | Strike as the filler, Guard Break only to close a Window | PR open #540 (game-test red) | T7 |
| 23 | Oracle churn | Byte-equal oracle traces move on every sim change (FORCES-ROUTE-PLAN.md:14-15, D28) | Property asserts instead of re-records | in lane | T8 |
| 24 | balance check red | "fork parity > no fork option is dominated by a sibling ... [FAIL]" on docs-only #542 (job 119555052544), so the red comes from main's tree | Green on main | not started | N0 |

## 3. The slices

Each slice is one PR. "Owns" lists the only files it may change, plus its own row file under game/lanes/. "Must
not touch" names the files owned by a concurrent slice. Every slice is red-first and prints its proof line in the
host log or the test output. For the lanes running tonight, the file lists are their declared domains, and their own
PR is the authority on what they change.

### Running tonight (T)

- **T1 PARTY-SOLID**, fix/party-solid-no-fade, **#541 PR open, BLOCKED** (balance red, row 24). Owns: game/src/View/ActorView.cs, game/content/Keyrun.Content/Model/PartyFade.cs, game/content/Keyrun.Content/Model/CameraNearFade.cs, game/content/Keyrun.Content.Tests/CameraNearFadeTests.cs. Must not touch: CameraRig.cs or CameraFraming.cs (T2). **Flag:** #541 also edits game/CLAUDE.md, which game/lanes/README.md reserves for the fold lane. Move that line to its row file. Red-first: an ally between the camera and the Warden draws at 1000 permille. Proof: `FADE actor=N alpha=1000`. Deps: none.
- **T2 CAMERA-VERTICAL**, feat/camera-vertical-arg, **in lane** (no branch on origin). Owns: game/src/View/CameraRig.cs, game/content/Keyrun.Content/Model/CameraFraming.cs, and the CameraFraming tests. Must not touch: ActorView.cs (T1), game/src/Host/SimDriver.cs beyond the one argument parse. Red-first: with `--camera vertical`, the pitch is straight down and every living party member and engaged enemy projects inside the frame. Proof: `CAMERA framing mode=vertical`. Deps: none.
- **T3 BOSS-TEACHES** (R-BOSS-1), feat/boss-teaches-r-boss-1, **in lane**. Owns: the boss's health and cadence as game-side content under game/content/data/ (content/data/enemies/hollow-warden.json is D12-frozen, FORCES-ROUTE-PLAN.md:11-13), game/sim/Keyrun.Sim/Encounter/Boss.cs, and a new boss-length test in tools/Keyrun.Playtest.Tests. Must not touch: Aggro.cs or layout.json (T9, N2). Red-first: a seeded bot party at key 2 kills the boss between 1800 and 3600 ticks after its engage, and the log shows Cleave, Twins and Lock each at least twice, each with its bot answer (a sidestep, a Break-bar drain, a Combo). Proof: `BOSS length ticks=T cleave=N twins=N lock=N`. Deps: none.
- **T4 ABILITY-ANIM**, feat/ability-animations, **in lane**. Owns: game/src/View/ActorAnimator.cs, plus new view files for ability effects under game/src/View/. Must not touch: ActorView.cs until #541 merges. Red-first: each of the 15 class abilities and each enemy cast spawns its named effect on cast start or resolve. Proof: `VIEW vfx actor=N ability=X tick=T`. Deps: none.
- **T5 SIM-PATHING** (R-PATH-1), feat/sim-pathing, **in lane**. Owns: game/sim/Keyrun.Sim/Combat/Pathing.cs (new: an integer grid or box-portal graph over the walkable boxes, with fixed tie-breaks), game/sim/Keyrun.Sim/Encounter/Chase.cs (a chaser steps along the path, not the straight line), and the pathing tests. Must not touch: Aggro.cs, Run.cs (T9), Follow.cs (N8 consumes Pathing), and any Godot navigation. Red-first: a chaser whose target is around a corridor corner reaches it without being clamped at the wall, and two runs on one seed give the same path tick for tick. Proof: `PATH actor=N nodes=K`. Deps: none.
- **T6 SCOREBOARD-LIVE**, fix/poc-scoreboard-live, **in lane**. Owns: game/src/Hud/TimerSlack.cs, game/src/Hud/ForcesBar.cs, game/scenes/Hud.tscn. Must not touch: other HUD files. Red-first: in the default POC autorun, the timer, the "Deaths: N (-Xs)" line and "Enemy Forces x/1000" are drawn and change after a death and a kill. Proof: `HUD timer ...` and `HUD forces pts=` lines. Deps: none.
- **T7 WARDEN-FILLER**, fix/warden-filler-rotation, **#540 PR open, BLOCKED** (game-test red, balance red). Owns: game/src/Host/ScriptedWarden.cs, game/bots/Keyrun.Bots.Tests/WardenFillerStrikeTests.cs. **Collision:** #528 also edits ScriptedWarden.cs, so whichever PR lands second merges main forward and keeps both intents. Deps: none.
- **T8 ORACLE-PROPERTIES**, test/oracles-property-asserts, **in lane**. Owns: only the oracle-comparing tests under game/sim/Keyrun.Sim.Tests and game/bots/Keyrun.Bots.Tests (it turns byte-equal trace pins into property asserts). Must not touch: any fixture file (freeze, section 5). Red-first: each property fails on a planted wrong trace. Deps: none.
- **T9 MPLUS-PATROLS-2**, feat/mplus-patrols-2, **#528 PR open, BLOCKED** (balance red). Owns (from gh): Aggro.cs, Patrols.cs (new), Pulls.cs, Run.cs, RunTypes.cs, Backfill.cs, Layout.cs, LayoutValidator.cs, ContentLoader.cs, layout.json, abilities/patrol.json, LayoutPackGroups.cs, HostRunSetup.cs, ScriptedWarden.cs, CastProof.cs, UnitFrameModel.cs, UnitFrames.cs, NameplateDeclutterTest.ps1, CastBarView.cs, and the tests. This is the chokepoint. N1, N2, N3 and S3/S5/S7 of FORCES-ROUTE-PLAN.md wait for it to merge. Deps: none.
- **T10 S2 VAULT-FORCES**, feat/s2-vault-forces, **in lane** (no branch on origin). Owns: game/content/data/forces.json, game/content/Keyrun.Content.Tests/VaultForcesTests.cs (new). Red-first: Hollow Hound 35, Ash Hexer 80, Vault Brute 90, Bellows Keeper 70, Drowned Chorister 60 (BIBLE :422-426). Its file is disjoint from #528, so it can open now. The schema and layout follow-ons (S3, S5, S7) are the slices blocked behind #528. Deps: none.

### New (N), ordered so the most can run at once

- **N0 BALANCE-RED**, fix/fork-parity-main. Owns: game/bots/Keyrun.Bots.Tests/ForkParityTests.cs and the bot or tuning file that the failure names, and nothing else. Red-first: the failing parity case, reproduced locally at 3bb6eed5. Never fit green: no margin is loosened (LANE-CONTRACT s3). Deps: none. **Run it first**, because it holds every open PR.
- **N7 RINGS-VIEW**, feat/aggro-ring-view. Owns: game/src/View/AggroRingView.cs (new), game/src/View/DungeonBuilder.cs (adds the node). Must not touch: ActorView.cs, CastBarView.cs (T9). Red-first: every idle group draws a grey dashed ring at its radius, a link draws a dashed line, and a patrol's ring moves with it. Proof: `VIEW ring group=G r=MM`. No new scene test, because it uses the existing CombatViewSceneTest.ps1 log (freeze). Deps: none to start. It draws links after N1.
- **N1 PACK-LINKS**, feat/pack-links. Owns: Layout.cs (`links` on a pack), LayoutValidator.cs (an unknown link id fails the load), Aggro.cs:100 (pass the links), and PackLinkTests.cs (new). Red-first: damaging one linked pack engages its partner on the same tick. Proof: `pack_engage` for both packs on one tick. Deps: T9 merged.
- **N2 BRANCH-LAYOUT** (D45; it replaces FORCES-ROUTE-PLAN S7 under R-PACKS-1), feat/branch-layout. Owns: game/content/data/layout.json, plus the pinned-layout tests it must re-derive and never loosen (PathPackTests, OptionalBranchForcesTests, RouteContentTests, SkippablePackTests, PathPackRouteTests, SkippablePackRouteTests, NavLayoutRunTests, LayoutConfigTests), and BiblePacksLayoutTests.cs (new). The Fork landing after the pair-hall leads to West (Undercroft Steps, then Bellows Hall) and East (Reading Gallery and Cantor's Loft, then Sluice Gallery), and both rejoin at the seed-chamber (BIBLE :331-336). There is no seal and no door (D44, R-PACKS-2/3). The slice places the section 4 packs and removes cantor-vigil, hall-sentries and warrior-remnant (R-PACKS-4/5). This is the one slice exempt from the oracle freeze: it re-measures `$ReplayTickDigest` once, with the reason "branch layout" (D28). Red-first: each 9.2 pack stands in its bible room with its members and points; both branches have a minimal set that reaches 1000; and a walker from the Fork can reach the seed-chamber through either branch. Deps: T9, T10, N1 (links in the layout), T5 (bots must path the fork).
- **N3 LEASH-RESET**, feat/leash-reset. Owns: game/sim/Keyrun.Sim/Encounter/Leash.cs (new), plus the one call site in Run.cs that T9 leaves, and LeashTests.cs (new). The default leash is 30000 mm from home, tuned later. Red-first: a mob dragged 1 mm past its leash walks home (pathing) at full health and its group returns to idle. On a wipe, every engaged living mob resets home and dead ones stay dead. Proof: `evade actor=N`. Deps: T9 (Run.cs), T5 (walk home).
- **N4 LOS-PULL**, feat/los-pull. Owns: game/sim/Keyrun.Sim/Combat/LineOfSight.cs (new: the segment stays inside the walkable box union, integers only), Chase.cs (after T5: a ranged caster with no line of sight to its target paths until it has one, then casts), and LosPullTests.cs (new). Red-first: a Drowned Chorister whose target steps around a corridor corner stops casting, walks, and resumes within reach in the target's sight. Proof: `los actor=N blocked` then `los actor=N clear`. Deps: T5.
- **N6 HAZARDS** (FORCES-ROUTE-PLAN S6), feat/hazards. Owns: game/sim/Keyrun.Sim/Encounter/Hazards.cs (new), EncounterStep.cs, game/content/data/hazards.json, game/content/Keyrun.Content/Model/Hazards.cs, HazardsTests.cs. Red-first: as FORCES-ROUTE-PLAN.md:426. Deps: N2 (the hazard rooms exist).
- **N8 BOTS-ROUTE** (FORCES-ROUTE-PLAN S9), feat/bots-route. Owns: game/bots/Keyrun.Bots/shared/RoutePlan.cs (new), Follow.cs, Ledger.cs, RoutePlanTests.cs, BranchRunClearTests.cs. Bots pick a branch and a minimal pack set, path around skipped rings, LOS-pull the Chorister pack, and follow the lowest living seat (D44). Red-first: a seeded key-2 bot run on N2 reaches 1000 or more and kills the boss, and every engage is a ring the plan chose. Deps: N2, T5, N4.
- **N5 FORCES-COMPLETION** (R-PACKS-3; FORCES-ROUTE-PLAN S8), feat/forces-completion. Owns: RunWiring.cs, Timer.cs, ForcesCompletionTests.cs. Red-first: a boss kill at 999 points does not finish the run, and the next trash kill that reaches 1000 finishes it on that tick. Deps: N8, because bots must reach 100% or every autorun stalls.

**Waves (the file overlap is the only barrier).** Now: N0, T1-T8, T10, N7, so 12 lanes with disjoint files.
When #528 merges: N1, N3 (also after T5), N4 (after T5). Then N2. Then N6 and N8 in parallel. Then N5.

## 4. The pack roster

Seven trash types plus the three teaching-pull groups. All kits are already loaded on main
(game/content/data/enemies/vault.json:2-60, content/data/enemies/choir.json:2-22). Telegraph radii are in mm and
angles in 1024ths of a turn. The points are BIBLE :417-426.

| Enemy | WoW role | Abilities (main) | Telegraph | Forces |
|---|---|---|---|---|
| Choir Acolyte (`choir.melee`) | filler melee | melee swing every 40 ticks (choir.json:10) | none (an auto-attack is exempt, BIBLE :46) | 40 |
| Choir Cantor (`choir.lieutenant`) | healer, must-kick | Choral Mending every 200 ticks (choir.json:20) | an open cast bar, the pack heal | 80 |
| Hollow Hound (`vault.hound`) | fast fragile melee; **the patrol** | swing every 150 ticks, speed 260 (vault.json:7-10) | none | 35 |
| Ash Hexer (`ash.hexer`) | must-kick caster | Searing Hex (dispellable burn) every 200 ticks; Cinders on death (vault.json:20-23) | circle 1200 under the target (needs #539's target anchor); death circle 2000, led by 40 ticks | 80 |
| Vault Brute (`vault.brute`) | bruiser with a frontal | Crushing Slam every 160 ticks, not kickable (vault.json:34-36) | cone 3500, 256/1024 (90 degrees), led by 40 ticks | 90 |
| Bellows Keeper (`bellows.keeper`) | enrager (Stoke buffs the pack; Purge removes it) | Stoke every 240 ticks (vault.json:46-47) | circle 4000 on itself, led by 70 ticks | 70 |
| Drowned Chorister (`drowned.chorister`) | ranged, **must be LOS-pulled** | Undertow pull and root every 180 ticks (vault.json:57-58) | cone 25000, 85/1024 (30 degrees), led by 60 ticks | 60 |

Teaching pulls on the critical path: Choir (2 Acolyte + 1 Cantor, 160), Pair (5 Paired Warrior, 260), and the Seed elite (180). Together they give 600.

| Pack | Branch, room | Members | Roles it tests | Points |
|---|---|---|---|---|
| W1 Hound Kennel | West, Undercroft Steps (**patrols**, BIBLE :346-348) | 4 Hound + 1 Hexer | peel, a kick, and a patrol joining a pull | 220 |
| W2 Bellows Crew | West, Bellows Hall (Vent Gout) | 2 Brute + 2 Keeper + 1 Hexer | frontals, purging the enrage, a kick | 400 |
| E1 Reading Watch | East, Reading Gallery | 2 Acolyte + 1 Chorister | the LOS pull | 140 |
| E2 Loft Choir | East, Cantor's Loft alcove (linked to E1 by N1) | 1 Acolyte + 1 Cantor + 1 Chorister | healer kick plus LOS | 180 |
| E3 Floodgate Watch | East, Sluice Gallery (Floodjet) | Brute + Acolyte + Chorister + Keeper | everything at once | 260 |

Route math (BIBLE :428-435). West: W2 alone reaches 1000; W1 alone reaches only 820, and W1+W2 is 1220 as a chain pull down the Steps. East: E3 plus E1 or E2 reaches 1000 or 1040. With no seal, a party may cross into the other branch, and the extra walking is paid on the timer. #528's corridor-2 patrol stays as it is (a second patrol, R-PATROL-3 governs its pace). R-PACKS-1's single path is superseded by D45, and S4's skip bar (two disjoint sets reaching 1000) still holds.

## 5. Freeze for one week (from merge of this plan)

- **No new scene tests** (`*SceneTest.ps1`, `*Test.ps1` under game/src). A slice proves itself with a log line in an existing scene test or with an xUnit test.
- **No oracle re-records.** No fixture under game/sim/Keyrun.Sim.Tests/Fixtures/, no move of E2eAssert's `$ReplayTickDigest`, and no new game/fixtures/input example. The one exception is N2, measured once with its reason. T8 removes the need for re-records.
- **No new layout packs outside N2**, and no layout.json edit outside T9 and N2.
- **No game/CLAUDE.md lane-list or BUILDLOG edits in a slice.** Use row files (game/lanes/README.md).
- **No Godot navigation** (R-PATH-1), and no new HUD panel (R-DECLUTTER-1).

## Notes for the orchestrator

- The D45 text on main (STATE.md:1548-1557) is the 2026-10-21 "tune later" list, not the branching ruling quoted in
  tonight's brief. D44 on main (STATE.md:1531-1539) still says "one forward path". The branching D45, R-BOSS-1,
  R-PATH-1, R-DECLUTTER-1, R-PATROL-3 and R-PACKS-7 are not on main at 3bb6eed5, and this plan takes them from the
  brief. The checkpoint should write them down.
- BIBLE 9.5 (:506-510, the Hollow Door) and 9.1's seal (:365-372) are superseded by D44 and R-PACKS-2/3, and are not built.
