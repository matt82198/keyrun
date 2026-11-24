# Keyrun redesign plan: Matt's eleven top-priority items as G5 stage slices

Status: S1-design, docs only, written 2026-10-25 against origin/main 8f4aae70. Matt asked for this plan, and it is
pending his approval. Items 1, 2, 4, 10 and 11 need no further design approval and can start now. Items 3, 5, 6, 8 and 9
change the design and wait for Matt to approve this file. Item 7 waits on 4, 5 and 9. Inputs: aesop event store
seq 39-49 (PLAN-CAPTURE 1-11, R-VIEW-HEAD-1, R-IDLEPACK-1), the orchestrator capture
`C:/Users/Mattt/Desktop/keyrun-screens/loop-8f4aae70` (101 frames, Hollow Vault +10 autorun on 8f4aae70), and
read-only code recon of 8f4aae70. Rules this plan stands on: core dungeon loop defers to WoW Mythic+ (BIBLE s9
principle); out of the dungeon (gear screen, transmog, customization) follows Dark and Darker within the BIBLE; ten
keybinds (BIBLE s2); determinism (CLAUDE.md invariant 1); R-PATH-1 (integer pathing in the sim); D45 (rooms branch);
D46 (seeded dungeons, reusable everything else); R-BOSS-1 (boss 90-180 s at key 2).

Paths: `Sim/` = `game/sim/Keyrun.Sim/`, `Bots/` = `game/bots/Keyrun.Bots/`, `Src/` = `game/src/`,
`Content/` = `game/content/Keyrun.Content/`, `gdata/` = `game/content/data/`, `cdata/` = `content/data/` (the content
the C# loader embeds alongside gdata). Line numbers are from 8f4aae70.

## 0. Summary

| # | Item | Kind | Verdict on main 8f4aae70 | Gate |
|---|---|---|---|---|
| 1 | Bots fight the boss on top of a pack | defect | Cause not yet named. Three candidates in the data: packs aggro on a 4000 mm ring only, the boss leash reaches into the Seed chamber, and the boss never assist-pulls trash. The first slice is a red property test that names the cause | now |
| 2 | Casters face random directions | defect | Partly fixed (#634, #667, #694). Gaps remain: cone, line and self casts, bot seats re-facing after cast start, and patrol and wander walk yaw | now |
| 3 | W/S forward and back, A/D strafe, right mouse turns | design | Movement is camera-relative, and without right mouse A, S and D turn the character | approval |
| 4 | No pathing; the dungeon is a straight line | defect | Confirmed. The Hollow Vault is nine boxes along +x with every route point at z=0, the validator enforces the chain, and with no obstacles the A* is inert | now |
| 5 | Packs too small, interrupt spam | design | Confirmed. Teaching pulls are 1 to 5 mobs with 6000 HP casters, and several Bellfoundry packs carry 3 or 4 kickers | approval |
| 6 | Gear obsession, everyone rolls, transmog and customization | design | Every seat rolls (done). Transmog, customization and Ash spending do not exist, and gear shows as primitive marks | approval |
| 7 | Intro = tutorial with bots up to +10 | design | The tutorial is the default dungeon and keys 2-20 exist, but bots do not reliably clear +10 and the current layout is rejected | after 4, 5, 9 |
| 8 | Defensives are percentages, immunity only on a major cooldown | design | No single button is 100%, but mitigation stacks additively and clamps to 0, so a Warden stack reaches 0 damage | approval |
| 9 | GCD too slow; no high-stakes movement | design | GCD is 30 ticks (1.5 s) for every rotational ability and haste does not shorten it. All three F buttons move, but no encounter demands them and no bot presses F | approval |
| 10 | Every idle pack alive | defect | IdleWander is on but drops shipped packs to a one-point loop, has no test, and its 1 m/s pace plays the run clip | now |
| 11 | Head glitches while moving | defect | Gear marks hang off the actor root at fixed heights, so the run bob slides the head through them (capture frames t40s, t54s) | now |

Slice order in ten lines:
1. Now, wave A, in parallel: 1.1 (red aggro property test, which names the cause), 2.1 and 2.2 (sim facing),
   2.3 (bot re-face), 4.1 (layout graph schema), 10.1 (IdleWander loops), 11.1 (gear marks on bones).
2. Now, wave B, after its wave-A twin: 1.2 (fix of the named cause), 2.4 (patrol and wander yaw, shared with 10.3),
   4.2 (obstacles to NavGrid), 10.2 (walk clip at wander pace).
3. Now, wave C: 4.3 (the tutorial from the seeded generator with a fixed seed), 4.4 (one-way door enforced),
   1.3 (boss-isolation layout rule).
4. After approval, wave D, in parallel: 8.1 (multiplicative mitigation), 9.1 (GCD 20 ticks, hasted), 3.1 (controls
   contract rule), 5.1 (pack budget validator), 6.1 (appearance collection in the save).
5. Wave E: 3.2 (input wiring), 3.3 (backpedal speed), 8.2 (immunity rule), 9.2 (bots press F and E on threats),
   6.2 and 6.3 (appearance catalog, bone-mounted meshes).
6. Wave F: 5.2 (Hollow Vault packs refilled to budget), 5.3 (Bellfoundry roster to budget), 9.3 (encounter M and D
   threats, the 14 open toolkit gaps), 6.4 (Appearance tab).
7. Wave G: 7.1 (tutorial ladder +2 to +10 with bots), 7.2 (first-run onboarding flow), 6.5 (Ash dyes).
8. Last: the fun acceptance test (section 13) and an orchestrator capture of one pull at +2 and +10.
9. Domains are file-disjoint inside each wave (section 1). Where two slices share a file they are serialized, and the
   section says so.
10. Every slice is a full G5 pipeline: S1 SPEC writes the named test red, S2 IMPL turns it green, then S3 GATE,
    S4 SHIP and S5 REVIEW.

## 1. Domains (file ownership)

Six domains from the brief, plus two the evidence needs. They are flagged in the table because the brief did not
list them.

| Domain | Owns | Test project |
|---|---|---|
| SIM-MOVE (sim movement) | `Sim/Encounter/{Aggro,EnemyAggro,Leash,IdleWander,Patrols,Chase}.cs`, `Sim/Combat/Movement.cs`, `Sim/Combat/Casting.Start.cs` (yaw block only, :86-97), `Sim/Nav/*` | game/sim/Keyrun.Sim.Tests |
| SIM-COMBAT (**added**: GCD and mitigation are not movement) | `Sim/Combat/{Cooldowns,Effects,Damage,Mitigation,Casting.Haste,Riposte}.cs` | game/sim/Keyrun.Sim.Tests |
| LAYOUT/CONTENT | `gdata/**`, `cdata/**`, `Content/Loading/**`, `Content/Model/Layout.cs`, `Sim/Dungeon/*` | game/content/Keyrun.Content.Tests, Sim tests for Dungeon |
| VIEW-ANIM | `Src/View/*`, `Content/Model/{AnimRule,ActorBinding}.cs` | Content tests (pure rules) plus scene tests run by game-e2e |
| BOTS-KIT | `Bots/**` | game/bots/Keyrun.Bots.Tests |
| INPUT | `Src/Host/PlayerInput.cs`, `Src/View/CameraRig.cs`, `Content/Model/MouseoverRule.cs`, new `Content/Model/MovementIntentRule.cs` | Content tests |
| GEAR-SCREEN | `Src/Hud/Screen.*`, `Sim/Run/{CompletionRoll,ProgressionSave,Ash}.cs`, `Src/HostCore/CharacterSheet.cs` | Sim, Content and scene tests |
| PLAYTEST (**added**: measurements and acceptance over full runs) | `tools/Keyrun.Playtest*` | tools/Keyrun.Playtest.Tests |

One shared file to watch: `Sim/Combat/Casting.Start.cs` holds both the cast-start yaw (:86-97, slice 2.1) and the GCD
start (:82, slice 9.1). Slice 9.1 changes `Riposte.GcdTicksFor` and `Cooldowns.cs` instead, so it never edits
Casting.Start.cs. If it must, it waits for 2.1 to merge.

Lanes never launch the game (no-game-launches-in-lanes). Every "capture" acceptance is run by the orchestrator, one
Godot at a time, with screens.ps1 on merged main at a named sha (seq 48 (d)).

---

## 2. Item 1: bots fight the boss on top of another pack (now)

**Current state on main.**
- Packs aggro on their group ring only. `Src/HostCore/HostRunSetup.Aggro.cs:43-46`: pulls without an authored ring
  get a per-member radius (`ownRadius: true`, 8000 mm from `gdata/run.json` `enemyAggro.aggroMm`), but layout packs
  and patrols are tuned with `ownRadius: false`. That leaves only the pack file's `aggroMm`: 4000 for
  `warrior-remnant` and `cantor-vigil`, 3000 for `hall-sentries` (`gdata/layout/hollow-vault/packs/*.json`).
- The boss leash is a 17000 mm circle around (93500, 0) (`gdata/run.json` kinds `hollowWarden.boss`, set by #698).
  The arena box starts at x 82000 (`rooms/090-warden-arena.json`), so the circle reaches x 76500. That is through
  corridor-4 and into the Seed chamber (x 62000-76000), 7.5 m from the `warrior-remnant` centre (69000, 5500).
- An engaged boss never assist-pulls trash (`Sim/Encounter/EnemyAggro.cs:94-100`, R-PATH-5 (p), #700). The boss has
  aggro 0 and assist 0.
- The Seed's per-member ring (73500 + 8000 = 81500) ends 4900 mm from the boss ring edge (93500 - 7100 = 86400),
  which leaves a gap the width of a corridor.
- Engage rule: `Sim/Encounter/Aggro.cs:95-111`. An idle group engages when a living seat is inside its ring, when a
  member takes damage, or when the group can see a player. Evading groups cannot engage (:101).
- Bot tank reach: `Bots/shared/ChainPull.cs:28` ReachMm 25000, :34 PartyLeashMm 12000.
- Capture `loop-8f4aae70/loop-079-t070s.png` (+10, 4:31): the HUD reads "Pull 4/4 The Hollow Warden / Defeat the
  pack: 7 of 14 remain". The party is fighting six Paired Warriors and a Choir Cantor (`warrior-remnant`
  composition), and the Hollow Warden is visible in the background.
- Tests that pin today's rules: `Keyrun.Sim.Tests/Encounter/AggroChaseTests.cs` (:87, :106, :125, :179, :229),
  `AssistScopeTests.cs` (:83, :110, :125), `Keyrun.Bots.Tests/BossAggroRingTests.cs` (:65, :94, :136),
  `PairAggroSpacingTests.cs:73`.

**Target (WoW Mythic+).** A mob aggroes on any player inside its own aggro radius (about 20 yards at level parity),
measured from that mob, not from a pack centre. Pack-mates are linked, so one aggro pulls the pack. A boss is leashed
to its room: it evades when dragged out of the room, not when it leaves a circle that crosses a doorway. Trash is
placed so that a boss fought in its room never stands inside a trash pack's aggro radius. In a real dungeon, fighting
on top of a pack always pulls it. Seq 39 observable: no boss pull is fought within aggro range of an idle pack without
that pack engaging.

**Slices.**

| Slice | Domain | S1 failing test (file :: name) | S2 files | Depends |
|---|---|---|---|---|
| 1.1 cause | PLAYTEST | `tools/Keyrun.Playtest.Tests/BossFightAggroPropertyTests.cs` :: `No_tick_has_an_engaged_boss_and_a_living_seat_inside_an_idle_packs_aggro_radius`, run over seeds 1, 3 and 7 at keys 2 and 10. The failure message prints the tick, pack id, member id, distance, boss position and which of the three candidates holds (ring-only radius, boss outside its arena, assist skipped) | none (S1 is the trace; the red output is the named cause, seq 39) | none |
| 1.2 fix | SIM-MOVE plus LAYOUT/CONTENT, by the named cause | the 1.1 test, plus a unit pin per cause. Ring-only cause: `Keyrun.Content.Tests/EnemyAggroConfigTests.cs` :: `Layout_packs_aggro_per_member_at_the_run_radius`. Leash cause: `Keyrun.Sim.Tests/BossLeashTests.cs` :: `A_boss_dragged_out_of_its_arena_box_evades` | ring: `HostRunSetup.Aggro.cs` (packs get `ownRadius: true`, with the group ring as a floor). Leash: `Sim/Encounter/Leash.cs` (a boss leashes to its room box, not a circle). R-PATH-5 (p) stays: the boss itself never assists | 1.1 |
| 1.3 rule | LAYOUT/CONTENT | `Keyrun.Content.Tests/BossIsolationRuleTests.cs` :: `No_trash_member_stands_within_its_aggro_radius_of_the_boss_room` | `Content/Loading/LayoutValidator.cs` (new rule; also applied to seeded layouts in `Sim/Dungeon/SegmentFill.cs` placement) | 1.2, and 4.3 for the new tutorial |

**Acceptance.** Headless: 1.1 green on all six seed and key runs. Capture: a +10 run's boss pull, from engage to
death, shows no trash nameplate in idle state within 8 m of any seat.

**Note.** The ruling forbids any aggro or leash tuning PR before 1.1 names the cause (seq 39). If 1.1 is green on
main, the observed fight came from a path the property misses (for example a pack already engaged and kited along).
The lane then reports that and widens the property. It never tunes blind.

---

## 3. Item 2: casters face random directions (now)

**Current state on main.**
- Facing fixes already landed: #634 (7ff8995a, view: every actor faces its target for the whole cast and attack),
  #667 (ac848fb0, sim: enemy Target and Circle casts write Yaw at cast start, `Sim/Combat/Casting.Start.cs:86-97`),
  and #694 (a5b4595d, bots: a zero-Dir MoveCommand faces the target at cast start, `Bots/shared/CastFacing.cs`).
- What still leaves a body facing away:
  1. Cone, line and self casts keep the sim Yaw by design (`Src/HostCore/ActorFacing.cs:31-32`,
     `Combat/CastFacing.cs:17-19`), and an enemy cone cast is never aimed at its target at cast start (#667 deferred
     it: "aiming a cone at its target at cast start moves oracle timelines and is a later ruling").
  2. A bot seat turns only on the start tick (`CastFacing.cs`). If the target moves during a 20-30 tick cast, the
     seat keeps its old yaw.
  3. Patrol members are moved with `a.Pos` only (`Sim/Encounter/Patrols.cs:137-143`), so they walk backwards or
     sideways. Wander sets yaw only when a leg yaw exists (`IdleWander.cs:110-115`).
  4. Pack enemies spawn at yaw 0 (`Src/HostCore/ContentToSim.Dungeon.cs:95`). An unengaged enemy keeps it
     (`ActorFacing.cs:66`).
- Tests: `Keyrun.Sim.Tests/ActorFacingTests.cs`, `CastFacingTests.cs`, `SwingFacingTests.cs`, `CastStartYawTests.cs`,
  `Keyrun.Bots.Tests/BotCastStartFacingTests.cs`. Nothing pins the drawn body yaw in `Src/View/ActorView.cs:800-805`.

**Target (WoW).** A casting mob faces its target for the whole cast. A frontal (cone or line) turns to its target on
the cast-start tick and then locks, which is what lets the tank aim it away and lets others step out. A mob in combat
faces its current target between casts. A walking mob faces where it walks. Seq 40 observable: every caster, enemy
and bot seat, faces its cast target for the whole cast and faces its current target while idle in combat.

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 2.1 frontal aim | SIM-MOVE | `Keyrun.Sim.Tests/CastStartYawTests.cs` :: `An_enemy_cone_or_line_cast_turns_to_its_target_on_the_start_tick_then_locks` | `Sim/Combat/Casting.Start.cs:86-97` (extend to Cone and Line); re-record the moved oracle traces under R-ORACLE-1, each naming this test | none |
| 2.2 track during cast | SIM-MOVE | `Keyrun.Sim.Tests/CastStartYawTests.cs` :: `An_enemy_target_cast_keeps_facing_a_moving_target_every_tick` | `Sim/Encounter/Chase.cs` (a casting enemy turns in place toward a Target-shape target each tick; frontals stay locked) | 2.1 (same test file) |
| 2.3 bot re-face | BOTS-KIT | `Keyrun.Bots.Tests/BotCastStartFacingTests.cs` :: `A_bot_seat_faces_a_moving_target_on_every_tick_of_its_cast` | `Bots/shared/CastFacing.cs`, `Bots/Bot.cs` (one zero-Dir face command per casting tick when the yaw error is above 16/1024) | none |
| 2.4 walk yaw | SIM-MOVE | `Keyrun.Sim.Tests/Encounter/PatrolTests.cs` :: `A_patrol_member_faces_its_walk_direction` and `IdleWanderTests.cs` :: `A_wandering_member_faces_its_leg` | `Sim/Encounter/Patrols.cs:137-143`, `IdleWander.cs:110-115` | 10.1 (same IdleWander file) |

**Acceptance.** Headless: a new `tools/Keyrun.Playtest.Tests/CasterFacingPropertyTests.cs` ::
`Every_enemy_and_bot_target_cast_faces_its_target_within_16_of_1024_on_every_tick`, over a Hollow Vault +2 seed 1 run.
Capture: ten frames of the first caster pull at +2 show every casting body turned to its target.

---

## 4. Item 3: controls, WoW mouse steering (waits for approval)

**Current state on main.**
- WASD is polled in `Src/Host/PlayerInput.cs:44-47, 225-228`. There is no Godot InputMap.
- Movement is camera-relative: forward = (-sin yaw, -cos yaw), right = (cos yaw, -sin yaw) (`PlayerInput.cs:231-239`).
- Without right mouse the character faces the walk direction, so A, D and S turn it (`MouseoverRule.cs:136-144`, used
  at `PlayerInput.cs:249-250`). With right mouse held it faces the camera heading, and A/D strafe (test
  `MouseoverRuleTests.cs:101-112`).
- Right mouse is mouselook and captures the cursor only while held (`PlayerInput.cs:123-130, 197-202`). Left click
  targets (:193-196).
- The sim already separates facing from movement: `MoveCommand(Tick, ActorId, Dir2 Dir, int Yaw)`
  (`Sim/Core/Commands.cs:22`). The Yaw snaps (`Movement.cs:85-100`), and there is no backpedal speed (180 mm/tick
  every way).
- There is no unit test of `PlayerInput.SampleMove` or `CameraRig.Look`.

**Target (WoW, Matt seq 41).**
- W runs forward along the character's facing. S backpedals (WoW backpedal is about 64% of run speed). A and D
  strafe and never turn.
- Holding right mouse turns the character with the camera (character yaw = camera yaw every frame). Holding left
  mouse orbits the camera without turning the character. Holding both runs forward.
- Without a mouse button held, the character keeps its facing, and WASD moves relative to the character, not the
  camera.

**Design calls for Matt.** (a) Backpedal at 6400 bp of run speed (115 mm/tick), as WoW does. (b) Left-mouse drag
orbits the camera; a left click without a drag still targets. (c) Both buttons held runs forward, inside the
ten-keybind budget because it is a mouse chord, not a key.

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 3.1 contract | INPUT | `Keyrun.Content.Tests/MovementIntentRuleTests.cs` :: `A_and_D_strafe_and_never_turn`, `Right_mouse_held_sets_the_character_yaw_to_the_camera_yaw`, `Without_a_button_W_walks_along_the_character_facing`, `Left_mouse_drag_orbits_without_turning`, `Both_buttons_run_forward` | new `Content/Model/MovementIntentRule.cs` (pure: keys, buttons, character yaw, camera yaw in; Dir and Yaw out); `MouseoverRule.cs` facing lines removed | approval |
| 3.2 wiring | INPUT | `Keyrun.Content.Tests/MovementIntentRuleTests.cs` :: `PlayerInput_samples_through_the_rule` (a seam test over a fake key source) | `Src/Host/PlayerInput.cs` (SampleMove calls the rule), `Src/View/CameraRig.cs` (left-drag orbit) | 3.1 |
| 3.3 backpedal | SIM-MOVE | `Keyrun.Sim.Tests/MovementTests.cs` :: `Backpedal_moves_at_the_backpedal_speed` | `Sim/Combat/Movement.cs` (the component of Dir opposite Yaw is scaled), `gdata/run.json` `movement.backpedalBp` | approval |
| 3.4 docs | INPUT | none (docs) | `game/PLAY.md:44-54`, BIBLE s2 one line ("W forward, S backpedal, A/D strafe, right mouse steers") | 3.2 |

Bots are unaffected. They already send Dir and Yaw directly (seq 41). **Acceptance:** 3.1 green, and an orchestrator
capture of a human-input replay strafing a circle telegraph with A/D while the camera stays on the target.

---

## 5. Item 4: pathing, and a dungeon that is not a straight line (now)

**Current state on main.**
- The Hollow Vault is nine boxes in one chain along +x (`gdata/layout/hollow-vault/rooms/010-090`, entrance x -6000 to
  the arena at 82000-110000), and every route point is at z=0 (`gdata/layout/hollow-vault/dungeon.json`).
- The validator enforces the chain: each box must continue along +x and overlap the previous one
  (`Content/Loading/LayoutValidator.cs:8, 60-66`). SideBoxes are empty for the hand-built vault (`Content/Model/Layout.cs`).
- The fork exists only as forces data (`gdata/forces/hollow-vault.json:5-6`, `forkAfterPull: "pair"`). Pack `branch`
  fields name rooms in the same chain (`packs/cantor-vigil.json`, `warrior-remnant.json`).
- R-PATH-1 pathing exists: `Sim/Nav/NavGrid.cs` (500 mm cells, integer A* with fixed tiebreak, :331, :463). It is
  used by chase, leash, wander and bots. But the vault layout has no obstacles (`Sim/Encounter/Pulls.cs:309-310`), so
  every path is a straight line (`NavGrid.cs:333`, the line-of-sight shortcut).
- The seeded generator does branch (`Sim/Dungeon/RoomGraphGen.cs:24-80`: one fork per segment, two branches of 2-3
  rooms, OneWayDoor edges at :74). It is used at runtime for multi-segment dungeons (`Bots/shared/SeededLayout.cs:209-211`,
  `Src/HostCore/HostRunSetup.cs:201-206`), but nothing closes a one-way door after entry (`RoomGraph.cs:37-40` defers
  it to "slice G6").
- BIBLE 9.1-9.2 describe West and East branches with packs W1, W2 and E1-E3, and design/TUTORIAL-ROUTE.md describes
  `via` points and corridor packs. Neither exists on main (section 14).

**Target.** WoW M+ dungeons are never one corridor: the route turns, side rooms hold optional packs, there is at
least one real choice of path, and pillars, walls and stairs shape pulls and line of sight. Seq 42 observable: on a
capture of merged main the tutorial route has turns, side rooms and at least one choice of path, and actors path
around geometry instead of walking straight lines. D45 and D46: rooms branch, and dungeons come from the seeded
generator.

**Approach (recommended).** Build the tutorial from the same seeded generator as every other dungeon, with one
segment, a fixed seed and a tutorial roster that pins the three teaching beats in order (BIBLE 9.6, 9.8). This retires
the hand-built chain and its +x validator rule instead of adding a second layout system (D46: everything reusable
except the dungeons). Matt rejected the current layout (seq 45), so nothing of its geometry needs preserving.

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 4.1 graph schema | LAYOUT/CONTENT | `Keyrun.Content.Tests/LayoutGraphTests.cs` :: `A_layout_whose_boxes_turn_and_branch_loads` and `A_door_must_join_two_overlapping_boxes` | `Content/Loading/LayoutValidator.cs` (replace the +x chain rule with a door graph: boxes plus doors, connected, one entrance, boss reachable), `Content/Model/Layout.cs` | none |
| 4.2 obstacles | SIM-MOVE | `Keyrun.Sim.Tests/NavPathingTests.cs` :: `A_layout_pillar_reaches_the_grid_and_a_chase_paths_around_it` | `Sim/Encounter/Pulls.cs:309-310` (NavLayout carries layout obstacles), `Content/Model/Layout.cs` obstacle list (shared with 4.1, so serialized after it) | 4.1 |
| 4.3 seeded tutorial | LAYOUT/CONTENT | `Keyrun.Sim.Tests/Dungeon/TutorialLayoutTests.cs` :: `The_tutorial_route_has_three_turns_two_side_rooms_and_a_fork`, `Teaching_pulls_stay_in_order_on_every_branch`, `The_walk_line_is_longer_than_the_straight_line_by_at_least_20_percent` | `gdata/dungeons/hollow-vault.json` (seed, one segment, tutorial roster), `Sim/Dungeon/SegmentFill.cs` (pinned teaching slots), retire `gdata/layout/hollow-vault/` | 4.1, 4.2 |
| 4.4 one-way door | SIM-MOVE | `Keyrun.Sim.Tests/Dungeon/OneWayDoorTests.cs` :: `Crossing_a_branch_threshold_seals_the_other_branch` | `Sim/Nav/NavGrid.cs` (a sealed door is unwalkable from the tick it seals), `Sim/Run/Run.cs` (one call) | 4.1 |
| 4.5 bots on the graph | BOTS-KIT | `Keyrun.Bots.Tests/PathToBossTests.cs` :: `The_bot_party_reaches_the_boss_on_the_seeded_tutorial_without_a_stuck_window` | `Bots/shared/Follow.cs`, `PathMoves.cs` (route from the room graph, not the per-box x list) | 4.3 |

Tests that pin the straight chain (`LayoutConfigTests.cs:23`, `NavRouteTests.cs:122-177`, `RouteContentTests.cs:25`
and the TUTORIAL-ROUTE facts) are re-derived from the new layout data in 4.3's PR, never loosened.

**Acceptance.** Headless: 4.3 and 4.5 green, and a bot party clears the seeded tutorial at +2 on seeds 1-5. Capture:
the minimap-free third-person frames show at least one turn of 90 degrees, a side room with an optional pack, and the
fork doorway.

---

## 6. Item 5: big pulls, few but punishing kicks (waits for approval)

**Current state on main.**
- The Hollow Vault teaching pulls: Choir = 2 Acolyte + 1 Cantor (`cdata/dungeons/hollow-vault.json:9-10`), each 6000
  HP (`cdata/enemies/choir.json:14-16`). The Cantor's Choral Mending is kickable, cast 80, cooldown 200
  (`cdata/abilities/enemies.json:28-39`). The Pair is 5 Paired Warriors. The Seed is one 3000 HP elite.
- Optional packs: `cantor-vigil` = 3 Cantors (3 kickers) + 4 Acolytes, all 6000 HP. `warrior-remnant` = 6 Warriors
  (1200) + 1 Cantor. `hall-sentries` = 5 Sentries (2000, melee only).
- Seq 49 capture: the Choir pull took over 70 s at +10 with 0 kills (the Cantor showed 9144 HP).
- Bellfoundry roster packs are 5-6 mobs (`gdata/roster/*.json`, pinned 5-8 by `SunkenBellfoundryPackSizeTests.cs:16`),
  with up to 4 kickers in one pack (bellows-shift, brine-chapel, furnace-stokers). drowned-choir carries 3 Knell
  Singers casting Drowned Dirge every 180 ticks.
- BIBLE 9.3 makes the kick economy the pack puzzle: 3 kicks per 24 s against more casters than that.

**Target (WoW M+, Matt seq 43).** Trash packs of 4-8 mobs, chain-pulled by choice into 10-20 mob pulls and killed with
AoE. One or two "must-kick" casts per pack on a long cadence, and a missed one is felt. Other dangers are dodged
(frontals, circles), stopped with control, or answered with positioning and line-of-sight pulls. The design must
reward movement, positioning, pulling and exploring. The Choir-style "three high-health casters and a kick every few
seconds" composition is superseded (seq 43 (d)).

**Budgets proposed for Matt (each becomes a validator rule).**

| Budget | Value | Why |
|---|---|---|
| Pack size | 4-8 mobs; teaching pulls 4-6; at least one chain of 2 packs (10+ mobs) available per segment | WoW trash packs; "huge pulls are fun" (D66) |
| Health | Pack total HP = party AoE damage at key 2 x 25 s, measured by slice 5.2 (not guessed). No trash mob above 2.5 x the pack mean | A pack dies in about 20-30 s of AoE. A single tanky caster cannot stall a pull |
| Kicks | At most 1 must-kick caster per pack (2 in a pack of 7-8); cadence at least 300 ticks (15 s); a pull of two packs needs at most 3 kicks per 24 s at key 10 | Three seats have three kicks per 24 s (BIBLE 9.3). Never kick spam |
| Punishment | A missed must-kick deals at least 4000 bp of a 250 seat's health, or heals or empowers the pack by at least 20% | Few kicks, each one matters |
| Mechanics | Every pack carries one positional threat (frontal, circle, on-death zone) and one toolkit threat from TOOLKIT-CONTRACT s3 (M1, M2, D1 or D2) | Movement and positioning are what a pull tests (seq 43 (c)) |

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 5.1 budget rule | LAYOUT/CONTENT | `Keyrun.Content.Tests/PackBudgetRuleTests.cs` :: `A_pack_with_more_must_kick_casters_than_its_budget_is_rejected`, `A_trash_mob_over_its_health_share_is_rejected`, `Shipped_packs_are_inside_budget` (red on main: cantor-vigil, bellows-shift, brine-chapel, furnace-stokers, drowned-choir, Choir) | new `Content/Loading/Rules/PackBudgetRule.cs`; the shrink-only known-violations file `design/pack-budget-gaps.json` (as toolkit-gaps does) | approval |
| 5.2 measure | PLAYTEST | `tools/Keyrun.Playtest.Tests/PullClearTimeTests.cs` :: `A_toolkit_bot_party_kills_a_budget_pack_in_20_to_30_seconds_at_key_2` | the measurement harness only; it records the party AoE DPS that sets the health budget | 9.1 (GCD changes DPS) |
| 5.3 tutorial packs | LAYOUT/CONTENT | `PackBudgetRuleTests.Shipped_packs_are_inside_budget` loses the Hollow Vault rows | `cdata/enemies/{choir,pair,seed}.json`, `cdata/abilities/enemies.json`, the tutorial roster from 4.3 | 4.3, 5.1, 5.2 |
| 5.4 Bellfoundry roster | LAYOUT/CONTENT | the same test loses the Bellfoundry rows | `gdata/roster/*.json`, `gdata/enemies/bellfoundry.json`, `gdata/abilities/bellfoundry.json` | 5.1, 5.2 |
| 5.5 bots chain | BOTS-KIT | `Keyrun.Bots.Tests/ChainPullTests.cs` :: `The_bot_tank_chains_two_budget_packs_inside_both_leashes` | `Bots/shared/ChainPull.cs` | 5.3 |

**Acceptance.** Section 13.

---

## 7. Item 6: gear obsession, everyone rolls, transmog and customization (waits for approval)

**Current state on main.**
- Everyone rolls: `Sim/Run/CompletionRoll.cs:59-75, 118-135`. Every player actor rolls with seed `rewardSeed*31+seat`
  (:123), called from `RunEndPhase.cs:36-41`. `gdata/loot/completion.json` sets the drop at 60% timed and 35% depleted.
  The run-end panel shows each seat's roll (#679).
- The Seed bonus roll of PROGRESSION-PIVOT s2.3 (seed `*31+s+16`) is not in CompletionRoll.cs or RunEndPhase.cs.
- Gear on the model is tier-coloured primitives (box, sphere) per slot (`Src/View/GearVisual.cs:54, 72-80`),
  attached to the actor root (item 11).
- No transmog, appearance, dye or customization system exists (only comments, `Ash.cs:12`, `Ascension.cs:22`). Ash is
  banked but nothing spends it. The screen tabs: `Src/Hud/Screen.Gear.cs` (reroll with crafting charges :26-45),
  `Screen.TabCharacter.cs`, `Screen.TabBuild.cs`.
- BIBLE s11 puts "cosmetics beyond colour swaps" out of scope for the POC.

**Target.**
- Dark and Darker: one lobby screen with tabs, and the character model shows what is equipped. WoW transmog: each
  slot's appearance is chosen from a collection that every looted item unlocks, separate from the item's stats.
- Matt (seq 44): gear is the long-term obsession, everyone rolls at the end of a dungeon, and customization and
  transmog are first-class.
- Art stays CC0 (D17). Appearances are KayKit and other CC0 meshes and palette variants, never custom art.

**Design calls for Matt.** (a) Amend BIBLE s11 so transmog and character customization are in POC scope. (b) Every
rolled item unlocks its appearance permanently, even when the player picks Leave. (c) Ash buys dyes (palette swaps per
slot) and customization options. Ash stays cosmetic only (BIBLE s7). (d) Customization at character creation covers
body model variant, skin palette and hair or helm variant, from the CC0 packs on disk.

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 6.0 Seed bonus roll | GEAR-SCREEN | `Keyrun.Sim.Tests/CompletionRollTests.cs` :: `A_combo_in_the_seed_window_gives_every_seat_a_second_roll` | `Sim/Run/CompletionRoll.cs` | none (closes a PIVOT gap; no approval needed) |
| 6.1 collection | GEAR-SCREEN | `Keyrun.Sim.Tests/AppearanceCollectionTests.cs` :: `Every_rolled_item_unlocks_its_appearance_even_when_left` and `ProgressionSaveTests` :: `A_v3_save_round_trips_appearances_and_migrates_v2` | `Sim/Run/ProgressionSave.cs` (v3), new `Sim/Run/Appearance.cs` | approval |
| 6.2 catalog | LAYOUT/CONTENT | `Keyrun.Content.Tests/AppearanceCatalogTests.cs` :: `Every_appearance_names_a_shipped_CC0_mesh_and_a_bone` | new `gdata/appearances.json`, `Content/Loading/Rules/AppearanceRule.cs`, `game/assets/ATTRIBUTION.md` rows | approval |
| 6.3 mount | VIEW-ANIM | `Keyrun.Content.Tests/GearMountRuleTests.cs` :: `An_equipped_appearance_mounts_on_its_slot_bone` | `Src/View/GearVisual.cs` (meshes on BoneAttachment3D from 11.1) | 11.1, 6.2 |
| 6.4 tab | GEAR-SCREEN | scene test `Src/Hud/scenes/AppearanceTab-SceneTest.ps1` :: `AppearanceTab: PASS` (pick an unlocked appearance per slot; the model updates) | new `Src/Hud/Screen.TabAppearance.cs` | 6.1, 6.3 |
| 6.5 dyes and Ash | GEAR-SCREEN | `Keyrun.Sim.Tests/AshSpendTests.cs` :: `A_dye_costs_Ash_and_never_changes_power` | `Sim/Run/Ash.cs`, `gdata/appearances.json` dyes | 6.4 |
| 6.6 roll moment | GEAR-SCREEN | scene test `RunEndRoll-SceneTest.ps1` :: every seat's roll card animates in seat order, and a drop shows its appearance on a model preview | `Src/Hud/RunEnd*.cs` | 6.3 |

**Acceptance.** Capture: run end shows three roll cards, one an item. The Appearance tab applies that item's look, and
the next run's frames show it on the character's bone, moving with the run cycle.

---

## 8. Item 7: the intro is the tutorial with bots up to +10 (after 4, 5 and 9)

**Current state on main.**
- The tutorial is the default dungeon (`Src/HostCore/DungeonPick.cs:19` DefaultId "hollow-vault"; hand-built, ignores the
  seed, :59).
- Keys are 2-20 at the screen (`Sim/Run/ScreenState.cs:62, 65, 132`; `KeyMove.cs:53`). `HostRunSetup.cs:184` only
  enforces key >= 1, and no test pins the range.
- Human plus bots is the default (`Src/Host/SimDriver.cs:247`, withBots = !Solo).
- Bots do not reliably clear +10: THREAT-MODEL :23-27, :41-43 (Warden dies four times at k10 s7, Depleted at tick
  10547), STATE.md:1312. The 8f4aae70 capture reached the boss at +10 by 5:41 with 131% forces, so the clear rate
  varies by seed.
- Matt (seq 45): the current dungeon design is rejected and must not be the template, and the combat feel is kept.

**Target.** The first session is the human plus two bots pushing the tutorial from +2 to +10 through the key upgrade
(BIBLE s8 chest tiers). The tutorial is 8-12 minutes at key 2 (BIBLE 9.8) and may be shorter than later dungeons.
WoW's equivalent is the level-up dungeon run before Mythic+, with Follower Dungeons (NPC party members) as the
precedent for bot partners. Each key teaches through its affixes: Fortified or Tyrannical from +5, both from +10
(BIBLE s8).

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 7.1 ladder | PLAYTEST | `tools/Keyrun.Playtest.Tests/TutorialLadderTests.cs` :: `A_guided_bot_party_with_a_scripted_human_seat_times_the_tutorial_at_keys_2_5_and_10_on_seeds_1_to_5` | tuning only, in the content the earlier slices own | 4.3, 5.3, 8.1, 9.1, 9.3 |
| 7.2 key range pin | GEAR-SCREEN | `Keyrun.Sim.Tests/ScreenStateTests.cs` :: `Keys_clamp_to_2_through_20_at_the_screen_and_the_setup` | `Src/HostCore/HostRunSetup.cs:184`, `Sim/Run/KeyMove.cs:53` | none |
| 7.3 onboarding | GEAR-SCREEN | scene test `FirstRun-SceneTest.ps1` :: `FirstRun: PASS` (a fresh save opens on the tutorial at +2 with bots and no other dungeon until +10 is timed once) | `Src/Hud/Screen.*`, `DungeonPick.cs` | approval of the unlock rule, 7.1 |

**Design call for Matt.** Other dungeons unlock when the tutorial is timed at +10. This is Keyrun's own rule; WoW
gates Mythic+ by level and item level.

---

## 9. Item 8: defensives are percentages; full immunity only as a major cooldown (waits for approval)

**Current state on main.**
- E buttons: Brace -4000 bp for 60 ticks, cooldown 300; Ashen Skin -3500 for 60, cooldown 300; Aegis -3500 for 60,
  cooldown 300, any ally (`cdata/abilities/warden.json:207`, `cinder.json:213`, `chanter.json:316`;
  `gdata/effects/player.json:14-27`). Rotational: Vigil -3000 for 100, Bulwark -1500 patch. Warden armor 35%,
  physical only (#644).
- No single button is 100%. Mitigation **adds** across effects: `Sim/Combat/Effects.cs:149`
  `taken += DamageTakenBp * stacks`. The damage stage then clamps at 0: `Sim/Combat/Damage.cs:33-37`
  `Math.Max(0, afterTarget)`.
- Warden Brace -4000, Vigil -3000, Bulwark -1500 and the keystone facet `warden.keystone.lastStand` (Brace also grants
  aegisShield -3500 and drops its cooldown to 200, `cdata/facets/warden.json:290-305`) sum to -12000. That is 0 damage
  for the Brace window, every 10 s.
- BIBLE 3.1 promises the full stack "never reaches immunity" ("15% x 65% = 9.75%, never 0"), so main breaks the bible
  here.
- The only intended 100% is the boss Lock shield (`cdata/effects/enemy.json:2-3`), which is enemy-side and stays.

**Target (WoW).**
- Damage reductions multiply: each applies to what the last one left, so stacking never reaches zero.
- Full immunities (Divine Shield, Ice Block) are major cooldowns of minutes, at most one per kit, with a cost.
- Tank one-shots are survived by armor plus an active defensive, never negated (seq 46).

**Design calls for Matt.**
- (a) damageTaken combines multiplicatively in a fixed effect-id order, as integer bp.
- (b) Floor: no stack of player effects takes more than 9000 bp off (at least 10% of every hit lands).
- (c) A full immunity is allowed only on an ability tagged `majorCooldown` with a cooldown of at least 3600 ticks
  (3 min), at most one per kit, and it must cost something (no attacking, or it drops threat). No class gets one now.
  This keeps the ten keys: it would live in an existing slot through a talent or tree node.
- (d) `lastStand` keeps its cooldown cut and loses the stacked Aegis (it becomes Brace +10% for 20 more ticks).

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 8.1 multiply and floor | SIM-COMBAT | `Keyrun.Sim.Tests/MitigationStackTests.cs` :: `Every_player_mitigation_stack_lets_at_least_10_percent_through` (red on main: Brace+Vigil+Bulwark+lastStand gives 0) and `Two_reductions_multiply_not_add` | `Sim/Combat/Effects.cs:140-150` (taken becomes a product), `Sim/Combat/Damage.cs`; oracle re-records name this test | approval |
| 8.2 immunity rule | LAYOUT/CONTENT | `Keyrun.Content.Tests/DefensiveBudgetRuleTests.cs` :: `A_full_immunity_needs_a_major_cooldown_of_3600_ticks` and `At_most_one_major_cooldown_per_kit` | new `Content/Loading/Rules/DefensiveBudgetRule.cs` (also checks facets and tree nodes) | 8.1 |
| 8.3 facet retune | LAYOUT/CONTENT | `Keyrun.Content.Tests/FacetTests.cs` :: `No_facet_stacks_a_second_damage_taken_effect_on_a_defensive` | `cdata/facets/warden.json` lastStand | 8.2 |
| 8.4 tank buster | LAYOUT/CONTENT | TOOLKIT-CONTRACT L12's red tests (the Hollow Warden Cleave as D2: dead without Brace or Vigil, alive with either) | per TOOLKIT-CONTRACT s5 wave 4 | 8.1, 9.2 |

**Acceptance.** Headless: 8.1 and 8.2 green. A key-10 Hollow Warden Cleave on a Braced Warden takes at least 10% of
its raw value in the combat log.

---

## 10. Item 9: pace (GCD) and high-stakes movement (waits for approval)

**Current state on main.**
- GCD: every rotational ability has `gcdTicks: 30` (1.5 s) (`cdata/abilities/warden.json:6, 44, 83, 120, 154`,
  same in cinder and chanter). `cdata/TUNING.md:8-9`: "GCD is fixed at 30 ticks for every class".
- Haste does not shorten the GCD. The GCD starts from `Riposte.GcdTicksFor` with no haste (`Casting.Start.cs:82`), and
  haste applies to cast time only (`Casting.Haste.cs:9-27`).
- Cast times: Warden all instant except Bulwark 20. Cinder Ember Bolt 20, Flare 30, Cinderfield 20. Chanter Cadence 20,
  Hymn 20.
- Q, E, R and F are off the GCD for all three classes (gcdTicks 0). That already matches WoW.
- Movement buttons all move: Charge (displace 8000 toward the target, stun 20; no root or slow removal), Blink
  (displace 8000 along facing, frees binds), Glide (+5000 bp for 60, frees binds). All have 2 charges and cooldown 200
  (`cdata/abilities/{warden,cinder,chanter}.json`; #645, #668, #689).
- Nothing demands them. `design/toolkit-gaps/encounter-gaps.json` still lists 14 gaps: no encounter has a threat
  lethal without F or without E. `design/toolkit-gaps/class-gaps.json` lists Charge not freeing binds.
- No bot presses F, and only the tank presses E, on a pressure threshold. `MovementAbilityId` is read nowhere in
  `Bots/` outside `Kits.cs:62, 197`; `DefensiveAbilityId` only in `Roles/Tank.cs:42-44` and `TankPressure.cs:83`.
- Seq 49 capture: the Choir pull ran over 70 s at +10.

**Target (WoW M+, Matt seq 47).**
- A 1.5 s base GCD that haste shortens to a 0.75 s floor. A typical M+ character plays near 1.0-1.1 s, and
  energy-based specs have a flat 1.0 s.
- Rotations are instant-heavy. Defensives and movement are off the GCD.
- Every class has a movement button that the dungeon forces it to use under telegraphs (Volcanic, big circles,
  frontals, roots).

**Design calls for Matt.**
- (a) Base GCD 20 ticks (1.0 s) for every class: the felt WoW pace without needing a haste stat.
- (b) Haste shortens the GCD with a floor of 15 ticks (0.75 s, WoW's floor).
- (c) Casts keep their ticks. Instants stay the majority (6 of 15 rotational casts have a cast time).
- (d) The TOOLKIT-CONTRACT s3 threat rule (M1, M2, D1, D2 on every pull and boss phase) is approved as the
  "dungeon demands it" half. Its tutorial calls stand (TOOLKIT s7 call 2: the tutorial is lethal too).
- (e) Warden Charge frees roots and slows and works with no target (TOOLKIT s7 call 4).

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 9.1 GCD | SIM-COMBAT, then CONTENT | `Keyrun.Sim.Tests/GcdTests.cs` :: `The_base_gcd_is_20_ticks`, `Haste_shortens_the_gcd_to_a_15_tick_floor`, `Riposte_still_frees_the_strike_after_brace` | `Sim/Combat/Riposte.cs` (`GcdTicksFor` applies haste with the floor), `Sim/Combat/Cooldowns.cs`; then `cdata/abilities/*.json` gcdTicks 20 and `cdata/TUNING.md:8-9` | approval |
| 9.2 bots use the toolkit | BOTS-KIT | TOOLKIT-CONTRACT L7: `Keyrun.Bots.Tests/BotToolkitTests.cs` :: `A_bot_in_a_blink_only_circle_survives_with_F_and_dies_without_it`, `A_bot_presses_E_before_an_unavoidable_spike_resolves` | new `Bots/Roles/Toolkit.cs`; one call each in `Roles/Tank.cs`, `Healer.cs`, `Damage.cs`; the guided guides | approval |
| 9.3 encounter threats | LAYOUT/CONTENT | TOOLKIT-CONTRACT L9-L13 red tests, one lane per encounter file; `encounter-gaps.json` shrinks to 0 | per TOOLKIT-CONTRACT s5 wave 4, plus the new tutorial roster from 4.3 and 5.3 | 9.2, 5.1 |
| 9.4 Charge binds | LAYOUT/CONTENT | `Keyrun.Content.Tests/ToolkitContractRuleTests.cs` :: `Charge_frees_binds` (class-gaps.json shrinks to 0) | `cdata/abilities/warden.json` charge | approval |
| 9.5 pace measure | PLAYTEST | `tools/Keyrun.Playtest.Tests/PaceTests.cs` :: `A_toolkit_party_casts_at_least_55_rotational_abilities_per_minute_per_seat_in_combat` | none (measurement) | 9.1 |

Raising the cast rate by 50% raises DPS. Slice 5.2 measures the health budget after 9.1, so the two never fight.

---

## 11. Item 10: every pack alive while idle (now)

**Current state on main.**
- IdleWander is on (`Src/HostCore/HostRunSetup.cs:289`, IdleWander = true) and stepped from `Sim/Run/Run.cs:254-258`.
- `IdleWander.Step` only moves a member whose loop has more than one offset (`Sim/Encounter/IdleWander.cs:96`), and
  Build drops shipped packs to a one-point loop (`IdleWander.cs:168-201`, R-IDLEPACK-1 seq 49 (b)). So shipped packs
  stand still.
- No IdleWander test exists (the lane doc's IdleWanderTests is absent; only `AssistScopeTests.cs` mentions the type).
- The wander pace is 50 mm/tick, or 1 m/s (`IdleWander.cs:32`). The view's move threshold is 0.4 m/s
  (`Src/View/ActorAnimator.cs:36, 240`), so a wandering mob plays Running_A, not a walk.
- A model with no AnimationPlayer or a missing clip renders static (`Src/View/ActorView.cs:210`, "clips=static").
- The patrol walks but never turns (`Patrols.cs:137-143`, item 2).
- Capture seq 49: the +6 Choir Cantor pack stands in a static row.

**Target (WoW).** Idle trash is never frozen. Mobs play idle loops with per-mob phase, some mill a few yards around
their post or work at a prop, and patrols walk their loop facing forward. A pack still reads as a pack: its ring stays
put.

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 10.1 loops | SIM-MOVE | `Keyrun.Sim.Tests/Encounter/IdleWanderTests.cs` :: `IdleWander_EveryShippedPackGetsAMovingLoop` (seq 49) and `A_wander_loop_never_leaves_the_ring_or_the_walkable_grid` | `Sim/Encounter/IdleWander.cs:168-201` | none |
| 10.2 walk clip | VIEW-ANIM | `Keyrun.Content.Tests/AnimRuleTests.cs` :: `A_mob_below_1_5_m_per_s_plays_its_walk_clip_and_above_its_run_clip` | `Content/Model/AnimRule.cs:249`, `Content/Model/ActorBinding.cs:21-35` (Walking_A per enemy row), `Src/View/ActorAnimator.cs` | none |
| 10.3 no static model | VIEW-ANIM | `Keyrun.Content.Tests/ActorBindingTests.cs` :: `Every_enemy_row_resolves_an_idle_and_a_walk_clip` | `Content/Model/ActorBinding.cs` rows | 10.2 (same file) |
| 10.4 walk yaw | SIM-MOVE | slice 2.4 | slice 2.4 | 10.1 |

**Acceptance.** Headless: over the first 400 ticks of a +2 run, every idle pack has at least one member that moves at
least 500 mm, and none leaves its ring. Capture: five frames one second apart of an untouched pack show changed
positions or poses and no T-pose.

---

## 12. Item 11: the head glitches while moving (now)

**Current state on main.**
- Gear marks are primitives at fixed root-space offsets (`Src/View/GearVisual.cs:72-80`): armor box at y 1.2, charm
  sphere at y 1.5, weapon box at y 1.05. They are added as children of the ActorView root (`ActorView.cs:676-683`),
  not of a bone, so the run clip's bob moves the head and torso through them. Capture frames t40s and t54s show a flat
  box floating at the Warden's head and neck (seq 49 (a)).
- No bone attachment, look-at or skeleton code exists anywhere in `Src/`.
- A second, weaker candidate: Idle/Move flicker at the 0.4 m/s threshold with a 0.15 s blend, on a speed derived per
  frame (`ActorAnimator.cs:36, 38, 240`; `ActorView.cs:314-321`).

**Target.** Anything worn follows the bone it is worn on, as in WoW and Dark and Darker. Locomotion picks a clip with
hysteresis, so it never flickers between idle and run at a steady walk.

**Slices.**

| Slice | Domain | S1 failing test | S2 files | Depends |
|---|---|---|---|---|
| 11.1 bone mounts | VIEW-ANIM | `Keyrun.Content.Tests/GearMountRuleTests.cs` :: `GearVisual_AttachesToBones_NotRoot` (seq 49: each slot maps to a bone name present in every shipped party model: head for charm, chest for armor, right hand for weapon and ring, hips for keystone) and scene test `Src/View/scenes/GearMount-SceneTest.ps1` (every mark's parent is a BoneAttachment3D) | new `Content/Model/GearMountRule.cs`, `Src/View/GearVisual.cs`, `Src/View/ActorView.cs:676-683` | none |
| 11.2 hysteresis | VIEW-ANIM | `Keyrun.Content.Tests/AnimRuleTests.cs` :: `Locomotion_switches_only_after_the_speed_holds_past_its_band_for_3_frames` | `Content/Model/AnimRule.cs`, `Src/View/ActorAnimator.cs` | 10.2 (same files) |

**Acceptance.** Capture: 20 frames at 0.25 s of the human seat running a corridor show no mark detached from the body
and no head clipping through a mark.

---

## 13. Fun acceptance: what one pull must show

A pull is fun when it is big, cleared with AoE, fought on the move, and asks for a few kicks that matter (seq 43, 47).
`tools/Keyrun.Playtest.Tests/FunPullAcceptanceTests.cs` computes each line from the run's event log for the
**largest pull** of a guided bot party with a scripted human seat, on the seeded tutorial, seeds 1-5, at key 2 and key
10. The orchestrator then captures the same pull on merged main and checks the frames by eye.

| Line | Headless observable (event log) | Capture observable |
|---|---|---|
| Pack size | At least 8 enemies engaged at once (two packs chained by choice), and at least one pull per run reaches 10 | At least 8 hostile nameplates in combat in one frame |
| AoE | At least 50% of party damage in the pull lands on 2 or more targets in the same tick | AoE telegraphs or hit sparks on several mobs in one frame |
| Pull length | 20-40 s at key 2 and 25-60 s at key 10 from first engage to last death. No trash mob lives more than 45 s after its engage | Timer delta across the pull's frames |
| Movement used | Every seat moves at least 15 m during the pull. At least one F press resolves a `threat:mobility` telegraph (the seat is outside its shape on the resolve tick). At least one E press is active on a `threat:defensive` resolve tick | A Blink, Charge or Glide visible mid-pull, and a seat stepping out of a ground telegraph |
| Kicks | At most N = 3 must-kick casts start in the pull at key 2 and at most 5 at key 10. Each one missed costs a seat at least 4000 bp of max health or heals or empowers its pack | Kick bars on at most one caster per pack at a time |
| No kick spam | No 24 s window asks for more than 3 kicks (the party's whole kick budget, BIBLE 9.3) | |
| Stakes | Deaths at most 1 at key 2. At key 10, a seat with E and F stripped dies in that pull (the toolkit is necessary) | |
| Pace | Each seat casts at least 55 rotational abilities per minute in combat (9.5) | The GCD sweep on the bar turns over about once a second |
| Aggro sanity | No idle pack is engaged by a body standing inside its ring without its whole pack engaging, and no boss pull runs inside an idle pack's radius (1.1) | |

The test is S1 red on main (the Choir pull is 3 mobs, ran over 70 s at +10, and no F is pressed). It turns green only
after waves A-F. Its thresholds are raised with a measurement and never lowered to pass (no fitted green).

---

## 14. Where main's evidence contradicts Matt's description or the docs

Reported, not dismissed. These are disagreements in the evidence; none of them overrides Matt's request.

1. **Item 2 (facing).** Three facing fixes have landed (#634, #667, #694). Matt's report still holds for what they left
   out: enemy cones and lines, bot seats after the cast-start tick, and patrol and wander walking. The row stays open
   (seq 40).
2. **Item 8 (defensives).** No class button grants 100% immunity on main. Brace is -40%, and Ashen Skin and Aegis are
   -35%. The 0-damage case comes only from additive stacking with the Warden keystone facet (Brace + Vigil + Bulwark +
   lastStand Aegis = -12000 bp). So the "shield ability that avoids one shots" Matt saw is either that stack or Brace
   working as BIBLE 3.1 designed (armor plus Brace takes a 220 Cleave to about 86). Slice 8.1 fixes the stack, and
   8.4 makes the tank buster lethal without a defensive. Matt should check whether Brace at -40% is also what he
   means; if so, the immunity rule (8.2) does not cover it, and the fix is the tank-buster tuning (8.4).
3. **Item 9 (movement).** All three F buttons do move on main (displace landed in #645, data in #668 and #689).
   TOOLKIT-CONTRACT s0 item 2 ("Blink is a no-op") is stale. Matt's point stands on usage: no encounter demands F
   (14 open encounter gaps), and no bot ever presses it. Q, E, R and F are also already off the GCD.
4. **Item 1 (aggro).** The static data shows no trash ring touching the boss ring, and the boss never assists. Which
   of three candidates causes the fight is not proven, so slice 1.1 names it before any fix. Other facts that differ
   from the docs: BIBLE 9.1 still says the Seed activates on room entry (removed by R-SEED-2, #671). The default ring
   BIBLE 9.1 states (4000) is what packs actually use, while teaching pulls aggro at 8000 per member, so the two kinds
   of group aggro differently.
5. **Item 4 (straight line).** Matt is right, and the bible is wrong about main. BIBLE 9.1-9.2 describe West and East
   branches and eight packs (W1, W2, E1-E3) that do not exist in the C# layout. design/TUTORIAL-ROUTE.md (`via`,
   fork-sentries, stair-choir) never landed.
6. **Item 5.** The Bellfoundry packs are already 5-6 mobs. "Too small" is true of the tutorial's pulls (1 to 5),
   while "interrupt spam" is true of both dungeons.
7. **Item 6.** The PROGRESSION-PIVOT s2.3 Seed bonus roll is not implemented (slice 6.0).
8. **Item 7.** The 8f4aae70 capture shows a bot party reaching and fighting the boss at +10 with 131% forces. THREAT-MODEL
   records k10 s7 wiping. So bots can clear +10 on some seeds but not reliably. Slice 7.1 measures it over seeds 1-5.
9. **Repo rule.** keyrun CLAUDE.md says `content/data/*.json` "stays verbatim through phase 4A". Merged lanes already
   edit it (#689 and others), and slices 5.3, 8.3, 9.1 and 9.4 must edit it too. That line needs Matt's amendment, or
   the data must move to `game/content/data/` first.
10. **Locked decisions.** Nothing in this plan contradicts the D4x and R- block given at dispatch. R-BOSS-6 already
    leashed the Hollow Warden at 17000 mm. Slice 1.2 may change that circle to the arena box, but only if slice 1.1
    names the circle as the cause.

## 15. Calls Matt may overrule (collected)

1. Item 3: backpedal at 64% speed; left-drag orbits the camera; both buttons run forward.
2. Item 4: the tutorial comes from the seeded generator with a fixed seed, and the hand-built chain is retired.
3. Item 5: the five pack budgets in section 6.
4. Item 6: transmog and customization in POC scope (BIBLE s11 amended). Every roll unlocks its appearance, and Ash
   buys dyes.
5. Item 7: other dungeons unlock when the tutorial is timed at +10.
6. Item 8: multiplicative mitigation with a 90% floor; immunity only as a `majorCooldown` of at least 3 min, at most
   one per kit, none shipped now; lastStand loses its stacked Aegis.
7. Item 9: base GCD 20 ticks, hasted to a 15-tick floor; the TOOLKIT-CONTRACT threat rule and its tutorial-lethal
   call adopted.
8. Fun acceptance: N = 3 kicks per pull at key 2 and 5 at key 10, pulls of 8 or more, and 20-40 s pull length at key 2.
