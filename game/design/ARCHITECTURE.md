# Keyrun architecture: reusable components and seeded dungeons

Recorded as STATE.md D63, the plan of record (it implements the 2026-10-22 rulings-block entry "D46 Seeded
dungeons, reusable everything else"; main's ledger also carries an older D46 "MPLUS CLOCK ONLY" and D47, so this
document takes the next free id). Matt 2026-10-22, verbatim: "we need a video game productivity engineer to help out
architecturally, like make everything reusable, except the dungeons, and they should generate by seed honestly...
then we fill with enemies... like consider order of operations... everything can be reusable and quick, and we can
attack the rare edge cases once the game is testable per the objective". Earlier the same night, verbatim: "the code
is clearly spaghetti", "this does not feel like m+ at all, its like vampire survivors with kicks", "the rooms should
not force you to one path".

The objective this document serves: **one complete Mythic+ run Matt can play end to end** (BIBLE section 1: screen,
queue, timed dungeon, boss, end-of-run choice, screen). Every rule below is judged against that, and every edge case
that does not block it is deferred by name (section 3.1).

Standing rules this inherits, unchanged:
- The sim is authoritative, deterministic and integer (mm, ticks at 20 Hz, bp, forces points). Godot only renders.
- Rule 0 inside the dungeon: WoW Mythic+, unless the bible names a divergence (BIBLE section 9).
- D44: no gates, every pack prespawned, the run wipes when no living seated player remains, bots follow the lowest
  living seat. D45: forward-only, no backtracking, never linear; rooms branch, and the player chooses the route and
  which packs to pull. R-PATH-1: "Deterministic integer pathing in the sim, never Godot navigation" (STATE.md);
  the sim's pathing is integer A* (#551, merged), and generated dungeons give it `WalkGrid` cells to search.

## 1. The component model: content is data, rules are small, nothing edits a hub to add content

Today adding an enemy means editing ContentLoader.cs (an explicit file list), HostRunSetup.cs (858 lines),
SimDriver.cs (1297), ActorView.cs (811) and often Screen.cs (1582). Those five files, plus Run.cs and Casting.cs, are
where parallel lanes collide, and the oracle manifest and the scene-test scripts churn more than product code (36 and
41 commits on main since 2026-10-14 against 20 to 29 for the hubs). The fix is one rule:

**Adding content adds files. It never edits a shared file.**

### 1.1 Components (one kind per directory; abilities/, effects/ and enemies/ exist today, the rest are new)

| Component | Directory (game/content/data/) | One file holds | Read by |
|---|---|---|---|
| Ability | `abilities/` | one or more ability defs (cast time, cooldown, ops, telegraph id) | Casting, bots |
| Telegraph | `telegraphs/` | a shape (circle, cone, line, ring) with lead ticks, outline and fill tokens | Telegraphs, TelegraphView |
| Effect | `effects/` | auras and their ops (burn, slow, Stoked) | Effects |
| Enemy | `enemies/` | an enemy kit: health, speed, melee, ability ids, forces points, on-death ability | Pulls, Forces, ActorView |
| Pack | `packs/` | a roster entry: members (enemy id x count, formation offset), aggro mm, patrol flag | the dungeon filler |
| Affix | `affixes/` | a key-level rule as data: when it is on, which stat bp it moves, which hook it adds | Affixes, Clauses |
| Class kit | `classes/` | the five rotational abilities, Q/E/R/F, resource, talents | Casting, bots, HUD |
| Dungeon theme | `dungeons/` | a pack roster, a boss id, generator params, room dressing | the pipeline (section 2) |

Rules:
- **Loaded by directory.** The loader reads every `*.json` under each directory in ordinal path order (deterministic
  across machines). There is no per-file registration list to edit. The frozen TS-oracle files stay where they are and
  keep their explicit list until phase 4A; new content goes only into the directories.
- **Ids are global and namespaced** (`vault.hexer`, `vault.searingHex`). A reference names an id, never a path.
- **One validator per rule.** Each rule is its own small class implementing `IContentRule` (`Check(ContentSet) ->
  findings`) in `Keyrun.Content/Rules/`, discovered by reflection in type-name order. Examples: every dangerous cast
  has a telegraph; every referenced id exists; every enemy has a forces value; a pack's members exist. A new rule is a
  new file plus its test; ContentValidator.cs stops growing. Rules fail closed: a finding fails the load.
- **Behaviour is an op, not a branch.** A new ability effect is a new op type in `EffectOps` (one file per op, a
  registry keyed by op name built by reflection), never a new `case` in Casting.cs.
- **Views bind by id.** ActorView chooses a model, colour and nameplate from data (`ActorBinding`, already present),
  so a new enemy needs no view edit. A new telegraph shape is the one case that adds a view file.
- **The host composes, it does not know content.** HostRunSetup asks the pipeline for a `GeneratedDungeon` and the
  loader for a `ContentSet`, then hands both to `RunRoot.CreateRun`. It names no enemy, pack or ability.

### 1.2 What stays hand-made (Matt: "everything reusable, except the dungeons")

The dungeon is not hand-authored geometry. A dungeon theme is data (roster, boss, params), and its rooms come from the
seed (section 2). The boss fight is hand-designed content (BIBLE 9.6), placed by the pipeline's last stage.

## 2. The seeded dungeon pipeline (order of operations)

Each stage is a pure function over integers: `(seed, inputs) -> output`, no clock, no float, no shared state. Each
stage draws from its own PRNG stream (`DungeonPipeline.StageRng(seed, stage)`, xoshiro128** from Core/Prng.cs), so
changing how many draws one stage makes never reshuffles another stage. The order matters: geometry before enemies,
budget before fill, fill before patrols, boss last, because each stage only reads the ones before it.

| # | Stage | Input -> output | Rule | Test (game/sim/Keyrun.Sim.Tests/Dungeon/) | Status |
|---|---|---|---|---|---|
| 0 | Seed | run seed -> stage streams | one stream per stage | `A_different_seed_gives_a_different_graph` | built |
| 1 | Room graph | seed -> `RoomGraph` | exactly one fork, immediately after the entrance, into exactly two branches; each branch a chain of 2 or 3 trash rooms (seeded per branch); both converge on the boss and nowhere else; edges only point deeper; the entrance-to-branch edges are one-way doors (`RoomEdge.OneWayDoor`) | `Every_graph_forks_once_after_the_entrance_into_two_branches_that_converge_on_the_boss`, `A_branch_once_entered_never_reaches_the_entrance_or_the_untaken_branch` (seeds 1 to 50) | built |
| 2 | Walkable grid and corridors | graph -> `WalkGrid` | 1000 mm cells, square rooms in column bands, each branch on its own row; one corridor lane per edge in the gap; the two branches' corridors use opposite halves of the entrance and boss walls, so the branches share no cell outside those two rooms | `Grid_walks_from_entrance_to_boss`, `Grid_keeps_the_two_branches_apart_outside_the_entrance_and_boss_rooms`, `Same_seed_gives_identical_graph_and_grid_twice` | built |
| 3 | Pack slots by forces budget and route | graph, grid, roster -> `RoomQuota[]` | each trash room's quota = ceil((2 x required + biggest pack) / rooms in its branch); up to six slots per room | `Every_route_reaches_full_forces_by_two_disjoint_pack_choices` | built |
| 4 | Fill from the pack roster | seed, quotas, roster -> `PackPlacement[]` | seeded draw per slot, the biggest pack when a draw could not finish the quota | same test, plus `Filler_fails_closed_on_a_bad_roster` | stub |
| 5 | Patrol paths | grid, placements -> patrol loops | a patrol pack walks a closed waypoint loop inside its room at fixed mm per tick; the loop stays on walkable cells and never enters another pack's ring | `A_patrol_loop_stays_in_its_room_and_repeats_with_its_period` | next |
| 6 | Boss room | graph, grid, theme -> boss placement | the boss stands at the boss room's centre with its aggro ring inside the room; the room's entry corridor is the only way in | `The_boss_ring_lies_inside_the_boss_room` | next |
| 7 | Run layout | everything above -> the run's layout | converts to what `RunRoot.CreateRun` reads: walkable cells for pathing, packs as `PackGroup`s, the boss as the last pull | `A_generated_run_reaches_the_boss_and_full_forces_under_bots` | next |

Why the stage 3 budget is right: a route is one branch and visits every room of it, so every route carries at least
2 x required + biggest pack. Deal those packs one at a time to whichever side is lighter and the two sides end
within one pack of each other, so both reach `required`: two disjoint pack choices fill the bar on every route (D45:
the player chooses which packs to pull). The test checks this with an independent subset DP, not the filler's rule.
The factor 2 is the first cut; Matt tunes it once the game is playable (D44 "we will tune later").

The grid is the contract with the sim's pathing (R-PATH-1; integer A*, #551): `WalkGrid` is a row-major byte array
(1 walkable), `CellMm`, `OriginX/OriginZ` in mm, `IsWalkable(cx, cz)`, `Contains(Vec3)` and `CellCenter`. A* runs over
its cells; nothing in this pipeline edits the pathing files, and stage 7 is where the two meet.

Forward-only (BIBLE section 9, D45 "no backtracking") is part of the generator's contract, not a later run rule:
every edge points deeper, no edge enters the entrance, no edge joins the two branches, and the entrance-to-branch
edges are one-way doors, so from a branch's first room neither the entrance nor the untaken branch is reachable in
the graph (tested on seeds 1 to 50). The grid matches: the branches share no walkable cell outside the entrance and
boss rooms. What remains for G6 is only the runtime enforcement of the door: closing the entrance-side corridor
cells to movement once a seat enters a branch's first room, so a walker cannot turn back through the door.

## 3. Slices to a playable run (the objective)

Each slice is one lane and one PR with a red test first, file-disjoint from every other slice in its wave. "After"
names the slices whose merged behaviour its red test needs.

| # | Slice | Owns (new files unless marked) | Red test | After |
|---|---|---|---|---|
| G1 | SEEDED-GRAPH-GRID-FILL (this PR) | `game/sim/Keyrun.Sim/Dungeon/*`, `Keyrun.Sim.Tests/Dungeon/SeededDungeonTests.cs`, this file | the eight SeededDungeonTests | none |
| G2 | DUNGEON-PATROLS (stage 5) | `Dungeon/PatrolPaths.cs`, `Tests/Dungeon/PatrolPathTests.cs` | loop stays in room, period repeats, ring never overlaps another pack | G1 |
| G3 | DUNGEON-BOSS-ROOM (stage 6) | `Dungeon/BossPlacement.cs`, `Tests/Dungeon/BossPlacementTests.cs` | boss ring inside the boss room for 400 seeds | G1 |
| G4 | PACK-ROSTER-DATA | `game/content/data/packs/vault.json` (BIBLE 9.2 W1-E3), `Keyrun.Content/Model/PackRoster.cs`, `Keyrun.Content.Tests/PackRosterTests.cs` | roster loads with bible members and points (220/400/140/180/260) | none |
| G5 | GRID-MOVEMENT (with feat/sim-pathing) | `Combat/GridWalkable.cs`, `Tests/GridWalkableTests.cs` | a move into a wall cell is clamped exactly as the box rule clamps today | G1 |
| G6 | RUN-LAYOUT (stage 7, wires the generator into the live run) | `Dungeon/RunLayout.cs`, `Run/RunTypes.cs` (one optional `RunContent` field), `Tests/Dungeon/RunLayoutTests.cs` | a seeded generated run spawns every pack idle at tick 0 and the bots reach the boss; once a seat enters a branch's first room the one-way door closes (a move back through it is clamped) | G2, G3, G4, G5 |
| G7 | FORCES-COMPLETION (plan slice S8) | `Run/RunWiring.cs`, `Encounter/Timer.cs`, `Tests/ForcesCompletionTests.cs` | a boss kill at 999 points does not complete the run | G6 |
| G8 | BOTS-ROUTE (plan slice S9) | `bots/.../RoutePlan.cs`, `RoutePlanTests.cs` | bots pick a route and a pack set reaching 1000 on 20 seeds | G6 |
| G9 | HOST-SEEDED-DUNGEON | `game/src/Host/HostRunSetup.cs` (one call), `game/src/View/GridDungeonBuilder.cs` (new) | the host builds floor tiles from the grid; orchestrator scene check names the harness | G6 |

Waves: A = G1. B = G2, G3, G4, G5 in parallel. C = G6. D = G7, G8, G9 in parallel. After D the run is playable
end to end on a seeded dungeon: queue, walk a branching route, choose packs, fill forces, kill the boss, run end.

### 3.1 Edge cases deferred until the run is playable (named so nobody builds them early)

- More than one fork, or branches longer than three rooms (the contract is one fork, two branches of 2 or 3, D63).
- Room size, shape and dressing variety (rooms are fixed squares). Room themes from `dungeons/` come later.
- Pack themes per room, linked chain pulls, and keeping two casters of the same kick out of one room.
- Forces overshoot tuning (every route carries at least 2x + one pack); the BIBLE 9.2 ~20% surplus is a later tune.
- Hazard rooms (BIBLE 9.4) placed by the pipeline; teaching pulls placed as fixed slots in the first rooms.
- Backfill joiners in a generated run (where a late seat spawns once the door has closed).
- Seed choice per week (affix week) and seed sharing; generator params per key level.
- Determinism across params changes: a params change re-rolls every seed; fixtures pin properties, never hashes.

## 4. Migration: today's hub files to the component model (file-disjoint slices)

Each row owns its files alone; no two rows in one wave share a file. Behaviour never changes in a migration slice:
the existing suites stay green unmodified, and the oracle fixtures are untouched (D28) because no rule moves.

| # | Slice | Owns | Proof (red first) | Wave |
|---|---|---|---|---|
| M-A | CONTENT-BY-DIRECTORY | `Keyrun.Content/Loading/ContentLoader.cs`, `Loading/DirectoryContentSource.cs` (new), `Keyrun.Content.Tests/DirectoryLoadTests.cs` (new) | a new `enemies/test-only.json` loads with no loader edit; the existing content set is byte-equal to today's | 1 (landed: #567, R-LOADER-1) |
| M-B | ONE-RULE-PER-FILE | `Keyrun.Content/Rules/*.cs` (new), `Loading/ContentValidator.cs` (shrinks to a runner) | each existing check moves to one rule file; a fixture breaking each rule yields exactly that rule's finding | 1 |
| M-C | EFFECT-OP-REGISTRY | `Combat/Casting.cs`, `Combat/Ops/*.cs` (new) | every op in content resolves through the registry; Casting has no op switch; CombatOracle tests stay green | 1 |
| M-D | RUN-STEP-PHASES | `Run/Run.cs`, `Run/Phases/*.cs` (new: movement, aggro, encounter, wipe, clock) | StepRun is a fixed list of phase calls; replay digests unchanged | 2 (after M-C) |
| M-E | HOST-COMPOSES | `game/src/Host/HostRunSetup.cs`, `game/src/Host/Setup/*.cs` (new) | a run set up with a test-only enemy and pack (data files only) starts with no HostRunSetup edit; HostQueuedRunTests green | 2 |
| M-F | DRIVER-SPLIT | `game/src/Host/SimDriver.cs`, `game/src/Host/Driver/*.cs` (new: input, replay, record, autorun) | each CLI seam moves to its own file; scene test logs unchanged (orchestrator runs them) | 3 (after M-E) |
| M-G | VIEW-BINDS-BY-ID | `game/src/View/ActorView.cs`, `game/src/View/Bindings/*.cs` (new) | a new enemy id renders from data with no ActorView edit (Godot-free binding test) | 2 |
| M-H | SCREEN-BANDS | `game/src/Hud/Screen.cs`, `game/src/Hud/Bands/*.cs` (new: Character, Key, Queue) | ScreenState tests unchanged; each band is its own file | 2 |
| M-I | ORACLE-PROPERTIES | owned by test/oracles-property-asserts | fixture hashes become property asserts, so content lanes stop rewriting the manifest | in flight |

Order: wave 1 removes the reasons content lanes touch hubs (loader list, validator, op switch). Wave 2 splits the
rule and view hubs so feature lanes land in new files. Wave 3 splits the driver last because every scene-test seam
runs through it. Slices G1-G9 (section 3) run beside the migration: G6 edits only `RunTypes.cs` and new files,
and G9 adds one call to HostRunSetup.cs, so G9 waits for M-E if both are open at once.

## 5. Coordination with lanes in flight

- **The hand-made layout** (`game/content/data/layout/`, split out of layout.json by #567; Layout.cs,
  LayoutValidator.cs): this pipeline does not edit them. Pack placement is never hand-placed in the layout files for a
  generated dungeon: it is stage 4, a fill step over the
  generated graph. G6 produces run content directly and leaves the hand-made layout as the default until Matt plays
  a seeded run.
- **Pathing (#551, merged)**: integer A* in the sim. It reads `WalkGrid` for generated dungeons (section 2); G5 is
  the joint slice where movement clamps to grid cells.
- **#546 docs/mplus-dungeon-plan is CLOSED** (it contradicted D46, seeded dungeons). It is not a plan
  of record; this document is (D63). Where a Mythic+ rule is in question, rule 0 (WoW Mythic+) and the BIBLE win,
  and the generator's params change.
- **test/oracles-property-asserts**: M-I above.
