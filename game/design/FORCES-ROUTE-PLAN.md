# Forces + route layer: build plan

The build increments that implement design/BIBLE.md section 9 (the 2026-10-20 forces + route amendment). Each
increment is one lane and one PR, with a red test written first. Increments that share no file can run in parallel.
An increment that shares a file with an earlier one waits for it (listed under "Depends on"). "Wait for" names the
open PRs (as of 2026-10-20, `gh pr list --repo matt82198/keyrun --state open`) whose files overlap the increment or
whose merged behaviour the increment's red test relies on.

Standing rules for every increment:

- C# only. The TS roots (`sim/`, `content/`, `bots/src/`, `client/`, `server/`) are frozen (STATE.md D12), and
  `content/data/*.json` is never edited. New content goes in `game/content/data/`, which shares the `content/data/`
  logical namespace (game/content/Keyrun.Content/Keyrun.Content.csproj). There is no TS twin. The C# sim is the
  source of truth (D28): oracle runs carry no forces, packs or hazards, so their fixtures stay byte-equal. If a
  fixture or E2eAssert's `$ReplayTickDigest` moves, regenerate it per D28 with the reason next to the new value.
- Purity: integers only in Keyrun.Sim and Keyrun.Content (points, mm, ticks, bp).
- Each lane writes its row file `game/lanes/<branch-with-dashes>.md` (game/lanes/README.md), never BUILDLOG.md,
  STATE.md or the game/CLAUDE.md lane list.
- Never fit green. A test that pins the old linear layout is re-derived for the new one by the increment that
  changes the behaviour, never loosened.

Parallel waves (from the dependencies): A = 0, 2, 5. A2 = 1. B = 3, 6. C = 4, 12. D = 7, 9. E = 8, 10. F = 11.

## 0. PRESPAWN-AGGRO: every group exists from tick 0 and engages on its aggro radius (bible 9.1)

- **Files:**
  - `game/sim/Keyrun.Sim/Run/Run.cs`: today pull 0 spawns at run setup (~63-70) and a layout pull spawns when the
    party is inside its room (~286-288, `PartyInside` then `Pulls.SpawnPull`). Instead, every group spawns idle at
    setup, and that layout step becomes an aggro-radius engage. `ActiveScripts` (~217, `Scripts.StartingAt`) is set
    from the engage tick. The D27 lock (~245-258) keys on engaged, not spawned.
  - `game/sim/Keyrun.Sim/Encounter/Pulls.cs`: `SpawnPull` is split into a run-start spawn of all groups and
    `Engage`, which emits `pull_start` on the engage tick. `IsSpawned` becomes `IsEngaged`. It adds the aggro check
    (radius, damage-taken, links).
  - `game/sim/Keyrun.Sim/Encounter/EncounterTypes.cs` (`PullDef.AggroMm`, `PullDef.Links`) and
    `game/sim/Keyrun.Sim/Encounter/EncounterState.cs` (the engaged-group set).
  - `game/sim/Keyrun.Sim/Encounter/Chase.cs`: an idle group neither chases nor targets.
  - `game/content/data/layout.json` (per-pull `aggroMm`), `game/content/Keyrun.Content/Model/Layout.cs` and
    `game/content/Keyrun.Content/Loading/LayoutValidator.cs` (radius inside the room for a teaching pull).
  - `game/bots/Keyrun.Bots/shared/Follow.cs`: `NavLayouts.PlaceDungeon` places every group at its formation,
    absorbing `PackOffset` from fix/pack-spawn-spread 3ab876fc. The walk avoids rings the plan does not pull, and the
    tank enters the rings it pulls first.
  - The TS twins `sim/encounter/pulls.ts` and `sim/run-wiring.ts`: frozen (D12). Edit them only if the oracle
    manifest requires it, then regenerate per D28.
  - `game/src/Host/E2eAssert.ps1` (`$ReplayTickDigest` re-measured per D28, reason "prespawn aggro").
  - Tests: `game/sim/Keyrun.Sim.Tests/Encounter/PrespawnAggroTests.cs` (new),
    `game/bots/Keyrun.Bots.Tests/PackFormationTests.cs` (from 3ab876fc), `game/bots/Keyrun.Bots.Tests/DeliberatePullTests.cs`
    (new), plus `RoomLockInTests.cs`, `NavLayoutRunTests.cs` and `Encounter/SeedOpenerTests.cs`, which are re-derived
    for engage-on-radius and never loosened.
- **Red test:**
  - At tick 0 every group's actors, the boss included, exist alive and idle at spread formation positions. None
    moves, casts or emits a telegraph until it engages.
  - A party member 1 mm outside the radius engages nothing. At the radius, the whole group engages and `pull_start`
    is emitted on that tick. Damage to one member engages its group, and a linked group engages with it.
  - Across every entry phase, each group's first telegraph comes after its engage tick (the Seed opener included).
  - A teaching pull's room gate shuts on its engage tick and opens on its clear.
  - In a seeded bot run, every engage is the tank (or the party following the tank) entering a ring the bots' plan
    chose to pull.
- **Depends on:** none in this plan. It comes before 1, because forces need prespawned groups.
- **Wait for:**
  - fix/enemy-engage 77284fc4 (edits `game/sim/Keyrun.Sim/Encounter/Chase.cs` and `sim/encounter/chase.ts`) must merge
    first.
  - Absorb fix/pack-spawn-spread 3ab876fc (`Follow.cs` `PackOffset`, `PackFormationTests.cs`; marked BLOCKED, not for
    merge on its own).
  - #283 and #274 hold the oracle fixtures and manifest, so land after them and measure the D28 regeneration once.

## 1. FORCES-SIM: the forces counter

- **Files:** `game/sim/Keyrun.Sim/Encounter/Forces.cs` (new), `game/sim/Keyrun.Sim/Encounter/EncounterTypes.cs`
  (`PullSpawn.ForcesPoints`, `DungeonDef.ForcesRequired`, both default 0), `game/sim/Keyrun.Sim/Encounter/EncounterState.cs`
  (`ForcesPoints`), `game/sim/Keyrun.Sim/Encounter/EncounterStep.cs` (credit on the death tick, emit `forces`),
  `game/sim/Keyrun.Sim/Encounter/EncounterJson.cs` (optional `forces` on a spawn),
  `game/sim/Keyrun.Sim.Tests/Encounter/ForcesTests.cs` (new).
- **Red test:** killing a spawn worth N adds exactly N on its death tick and emits one `forces {points, required}`
  event. A hazard kill counts. Boss, Twins and Echo adds add 0. A wipe or a death never lowers the counter. The counter
  is unchanged on a tick with no death. A dungeon with every value 0 emits no `forces` event, so the oracle fixtures
  stay byte-equal.
- **Depends on:** 0 (prespawned groups; EncounterTypes.cs and EncounterState.cs).
- **Wait for:** #283 and #274 (both rewrite `game/sim/Keyrun.Sim.Tests/Fixtures/oracle/manifest.json` and oracle
  traces; land after them so any D28 regeneration is measured once on the merged tree).

## 2. ROUTE-CONTENT-SCHEMA: forces, packs and hazards content files

- **Files:** `game/content/data/forces.json` (new: `required: 1000` and the teaching-pull values 40/80/52/180),
  `game/content/data/packs.json` (new, empty list), `game/content/data/hazards.json` (new, empty list),
  `game/content/data/enemies/vault.json`, `game/content/data/abilities/vault-casters.json`,
  `game/content/data/effects/vault.json` (new, each an empty array),
  `game/content/Keyrun.Content/Model/Forces.cs`, `Model/Packs.cs`, `Model/Hazards.cs` (new),
  `game/content/Keyrun.Content/Loading/ContentLoader.cs` (register all six files),
  `game/content/Keyrun.Content/Loading/ContentValidator.cs`,
  `game/content/Keyrun.Content.Tests/RouteContentTests.cs` (new), `game/content/Keyrun.Content.Tests/ContentCountTests.cs`
  (file count re-measured, never copied).
- **Red test:** the loaded forces table gives Choir 160, Pair 260 and Seed 180, total 600. The validator fails closed on
  a pack enemy with no forces value, on a duplicate pack id, on a pack in an unknown room, and on any branch whose
  critical path plus all of its packs is under `required`.
- **Depends on:** none.
- **Wait for:** none (no open PR touches these files).

## 3. PACKS-SIM: optional packs on increment 0's prespawn and engage

- **Files:** `game/sim/Keyrun.Sim/Encounter/Packs.cs` (new), `game/sim/Keyrun.Sim/Encounter/EncounterTypes.cs`
  (`DungeonDef.Packs`, `PackDef {id, room, center, aggroMm, links, spawns}`), `game/sim/Keyrun.Sim/Run/Run.cs` (packs
  join the run-start spawn and the engage step), `game/sim/Keyrun.Sim.Tests/Encounter/PacksTests.cs` (new).
- **Red test:** every pack exists idle from tick 0. It engages whole on the first tick a living party member is
  within its aggro radius, or when party damage or a hazard hits a member, and never otherwise. Walking past outside
  the ring leaves it idle (a skip). Entering two rings on one tick engages both (a chain pull). `Encounter.PullIndex`
  never moves for a pack. Pack kills credit forces through increment 1.
- **Depends on:** 0 (engage model), 1 (EncounterTypes.cs, forces credit).
- **Wait for:** none.

## 4. ROUTE-GATES: the branch seal and the Hollow Door

- **Files:** `game/sim/Keyrun.Sim/Encounter/RouteGates.cs` (new), `game/sim/Keyrun.Sim/Encounter/Pulls.cs` (NavLayout
  carries branch tags, seal doorways and the door), `game/sim/Keyrun.Sim/Run/Run.cs` (`StepLayout` clamps at sealed
  doorways and at the shut door through `GateClamp`), `game/content/Keyrun.Content/Model/Layout.cs` (box `branch`,
  `seals`, `door: "forces"`, a `routes` map with `route` kept as West), `game/content/Keyrun.Content/Loading/LayoutValidator.cs`,
  `game/content/data/layouts/hollow-vault-branching.json` (new: section 9.1 map, rooms, rings, stairs),
  `game/content/Keyrun.Content/Loading/ContentLoader.cs` (register the second layout; `layout.json` stays the
  default), `game/sim/Keyrun.Sim.Tests/BranchSealTests.cs`, `game/sim/Keyrun.Sim.Tests/HollowDoorTests.cs`,
  `game/content/Keyrun.Content.Tests/BranchingLayoutTests.cs` (new).
- **Red test:** a party member crossing the West threshold closes the East doorway and the Pair-hall doorway for the
  rest of the run, and an actor pushed at either is clamped. Walking back from the Seed chamber into West rooms is
  allowed, and the East stair stays shut. The door stays shut when the Seed is cleared at 999 points, and opens on the
  tick forces reach 1000 with the Seed cleared (in either order). The validator rejects a branching layout with no
  door, with a seal naming an unknown box, or with a branch that has no stair to the Seed chamber. It also enforces
  the density rule (bible 9.1): the validator rejects any walking-line stretch longer than 28800 mm (160 ticks at
  180 mm/tick) between engages, and any room on the walking line with no teaching pull and no pack ring.
- **Depends on:** 1, 2 (ContentLoader.cs), 3 (Run.cs).
- **Wait for:** none.

## 5. CASTER-OPS-SIM: the effect ops the new casters need

- **Files:** `game/sim/Keyrun.Sim/Combat/Effects.cs` (Stoked damage-up aura, Drenched slow, a dispellable magic burn
  if Purge and Cleanse cannot already remove it), `game/sim/Keyrun.Sim/Combat/Movement.cs` (Undertow pull toward the
  caster, walkable-clamped, 20-tick root), `game/sim/Keyrun.Sim/Combat/Casting.cs` (Stoked +20% cast speed),
  `game/content/Keyrun.Content/Model/EffectOps.cs`, `game/sim/Keyrun.Sim.Tests/CasterOpsTests.cs` (new).
- **Red test:** Undertow moves its target 5000 mm toward the caster, stopping at a wall, and roots it for 20 ticks.
  Stoked raises an enemy's damage by 3000 bp and shortens its casts by 2000 bp for 160 ticks, then ends. Searing Hex's
  burn is removed by Cinder's Purge and by Chanter's Cleanse. Drenched slows movement by 3000 bp for 60 ticks.
- **Depends on:** none.
- **Wait for:** #283 (edits `game/sim/Keyrun.Sim/Combat/Casting.cs`).

## 6. PACK-CONTENT: the five packs and their kits

- **Files:** `game/content/data/enemies/vault.json` (Hollow Hound, Vault Brute, Ash Hexer, Bellows Keeper, Drowned
  Chorister), `game/content/data/abilities/vault-casters.json` (Searing Hex, Stoke, Undertow, Crushing Slam, hound bite),
  `game/content/data/effects/vault.json`, `game/content/data/packs.json` (W1, W2, E1, E2, E3 per section 9.2),
  `game/content/data/forces.json` (new enemies' values), `game/content/Keyrun.Content.Tests/PackContentTests.cs` (new),
  `game/content/Keyrun.Content.Tests/EnemyLineTelegraphGuardTests.cs` (also sweep `game/content/data/enemies/`).
- **Red test:** each pack's members and point sum match section 9.2 (W1 220, W2 400, E1 140, E2 180, E3 260), and the
  route totals come to West 1220 and East 1180. The minimum fills hold: W2 alone 1000, E3 plus E1 1000, and W1 alone,
  E1 plus E2 and E3 alone all under 1000. Exactly four distinct interruptible enemy casts exist (Choral Mending,
  Searing Hex, Stoke, Undertow) with the cast times and cooldowns in section 9.3. Every dangerous cast has a circle or
  cone telegraph, and the existing DangerousCastTelegraphTests pass over the new files.
- **Depends on:** 2 (the files and loader), 5 (the ops).
- **Wait for:** #280 (tooltip text-matches-data guard; land after it so the new ability text is checked by the
  merged guard).

## 7. HAZARDS: Vent Gout and Floodjet

- **Files:** `game/sim/Keyrun.Sim/Encounter/Hazards.cs` (new: an untargetable environment caster per hazard room),
  `game/sim/Keyrun.Sim/Encounter/EncounterStep.cs` (call `Hazards.Step`), `game/content/data/hazards.json` (both
  hazards per section 9.4), `game/content/Keyrun.Content/Model/Hazards.cs`, `game/sim/Keyrun.Sim.Tests/Encounter/HazardsTests.cs`
  (new).
- **Red test:** Vent Gout emits set A's three 2500 mm circle telegraphs 60 ticks before they resolve, then set B
  160 ticks later. It hits a player inside for 30% of max health and an enemy inside for 15%, and does nothing while
  no party member is in Bellows Hall or after W2 is dead. Floodjet alternates nozzles every 200 ticks with a 90-degree
  10000 mm cone leading by 80 ticks, and applies Drenched to players and enemies. At key 10 the lead is
  Tempo-scaled and never under 10 ticks. A Q on the hazard actor does nothing. Under Tide the hazard does not drift.
- **Depends on:** 1 (EncounterStep.cs), 4 (room ids), 5 (Drenched).
- **Wait for:** none for the sim. #257 must merge before the hazards are judged visually, because it makes circle
  telegraphs draw.

## 8. HUD-FORCES: the Forces bar and the hazard frame

- **Files:** `game/src/Hud/ForcesBar.cs` (new: Godot-free `ForcesBarModel` plus the Control),
  `game/src/Hud/HazardFrame.cs` (new), `game/scenes/Hud.tscn`, `game/src/Hud/ForcesBarSceneTest.ps1` (new),
  `.github/workflows/game.yml` (one step after the existing scene tests).
- **Red test:** the scene test logs `HUD forces pts=0/1000 pct=0` at start and a higher pct after a kill, reads
  `full` at 1000 and `104%` at 1040, and shows the hazard frame's cast name and bar only while a hazard is casting.
  The bar sits under the Slack bar without overlapping it (HudClearance).
- **Depends on:** 1, 7.
- **Wait for:** #219 (edits `.github/workflows/game.yml`), #253 (edits `game/src/Hud/TimerSlack.cs`, the bar the
  Forces bar anchors under).

## 9. VIEW-ROUTE: branch rooms, aggro rings, seals and the door in the arena

- **Files:** `game/content/Keyrun.Content/Model/DungeonPlan.cs` (branch boxes, seal gate pieces, door piece, one
  route per branch instead of one route point per box), `game/src/View/DungeonBuilder.cs`,
  `game/src/View/RoomGateView.cs` (the door's "Forces 87%" readout), `game/src/View/AggroRingView.cs` (new: grey dashed
  ring), `game/content/Keyrun.Content.Tests/DungeonPlanTests.cs`, `game/src/Host/GateViewSceneTest.ps1`.
- **Red test:** DungeonPlan.Build on the branching layout gives a gate piece for each sealed doorway and the Hollow
  Door, floor tiles for every branch room, and no wall across the Fork landing's two exits. The gate scene test
  shows the sealed doorway closed after the seal tick and the door label with the shortfall percent. Pull size is
  read from the floor (bible 9.1): from each branch's doorway camera, every aggro ring in that branch is inside the
  view, which the scene test asserts by projecting each ring's centre to the screen.
- **Depends on:** 4.
- **Wait for:** #257 (edits `game/src/View/TelegraphView.cs` beside this view work; circles must draw before the
  hazard rooms are judged).

## 10. BOTS-ROUTE: bots pick a route that fills the bar and still clear

- **Files:** `game/bots/Keyrun.Bots/shared/RoutePlan.cs` (new: pick a branch and the fewest packs that reach 1000,
  enter those rings on purpose, skip the rest along the far wall, walk back on a shortfall),
  `game/bots/Keyrun.Bots/shared/Follow.cs` (WalkRoute and LeadRoute follow the RoutePlan's route),
  `game/bots/Keyrun.Bots/shared/Ledger.cs` (kick priority across several casters: Stoke, Undertow, Choral Mending,
  then Searing Hex, with Hex dispelled when it is not kicked), `game/bots/Keyrun.Bots.Tests/RoutePlanTests.cs` (new),
  `game/bots/Keyrun.Bots.Tests/BranchRunClearTests.cs` (new).
- **Red test:** RoutePlan picks W2 only on West and E3 plus E1 on East. A seeded bot run on the branching layout
  (key 2, seeds 7 and 11) seals one branch, reaches forces 1000 or more, opens the Hollow Door and kills the boss.
  In a pack with two casters the bots kick the higher-priority cast first, and the existing Bots suite stays green.
- **Depends on:** 3, 4, 6, 7.
- **Wait for:** #256 (guards the Choir kick ledger that Ledger.cs extends), #283 (rewrites
  `game/bots/Keyrun.Bots.Tests/RolesOracleTraceTests.cs` and the bot oracle traces).

## 11. ROUTE-ON: the shipped run uses the branching dungeon

- **Files:** `game/content/data/layout.json` (becomes the branching layout; `layouts/hollow-vault-branching.json`
  folds into it), `game/content/Keyrun.Content/Loading/ContentLoader.cs` (one layout again),
  `game/src/Host/HostRunSetup.cs` (forces values, packs and hazards into the sim dungeon), `game/src/Hud/Objective.cs`
  (the objective line names the current pull or engaged pack), `game/src/Host/E2eAssert.ps1` (`$ReplayTickDigest`
  re-measured per D28, reason "forces + route layer"), plus every test that pinned the linear layout:
  `game/sim/Keyrun.Sim.Tests/NavLayoutRunTests.cs`, `game/sim/Keyrun.Sim.Tests/RoomLockInTests.cs`,
  `game/content/Keyrun.Content.Tests/LayoutConfigTests.cs`, `game/bots/Keyrun.Bots.Tests/NavRouteTests.cs`,
  `game/bots/Keyrun.Bots.Tests/LeadRouteHoldTests.cs`, `game/bots/Keyrun.Bots.Tests/JoinerDoorwayTests.cs`
  (re-derived from the new layout, never loosened).
- **Red test:** the default host autorun seals a branch, logs the forces pct rising to 100, opens the Hollow Door
  and reaches the boss. The objective line names W2 while the Bellows Crew is engaged. E2eAssert passes on the
  re-measured digest (two runs measured equal).
- **Depends on:** 8, 9, 10.
- **Wait for:** #233 (edits `game/src/Host/HostRunSetup.cs`).

## 12. PACK-REWARD: what an optional pack pays (bible 9.1)

- **Files:** `game/sim/Keyrun.Sim/Run/Ash.cs` (Ash per optional pack killed), `game/sim/Keyrun.Sim/Encounter/Rewards.cs`
  (at 1100 points or more when the Hollow Door opens, one of the three end-of-run options is raised by one tier,
  capped at 10), `game/sim/Keyrun.Sim.Tests/Encounter/PackRewardTests.cs` (new).
- **Red test:** a run that kills two optional packs banks exactly two packs' Ash more than the same run with none.
  At 1099 points when the door opens no option is raised. At 1100, exactly one option is one tier higher. An option
  already at Tier 10 is never raised past it.
- **Depends on:** 1, 3.
- **Wait for:** #233 (edits `game/sim/Keyrun.Sim/Run/Ash.cs`), #250 (edits the Rewards tests).

## Next increments (outside the dungeon; from the same 2026-10-20 bible amendment)

These are not part of the forces + route build and get their own plan when they are dispatched. The file lists
are guesses.

- **CLAUSE-BY-KEY** (bible 8): the key sets and rotates the Clause, with none below the starting key, and the
  screen shows it. Files: `game/sim/Keyrun.Sim/Run/Clause.cs`, `game/sim/Keyrun.Sim/Run/RunOptions.cs`,
  `game/src/Hud/ClausePick.cs` (becomes a read-only readout), `game/sim/Keyrun.Sim.Tests/ClausePickTests.cs`.
- **REWARD-GUARANTEE** (bible 7): one of the three options is always an upgrade, or Ascension progress at the cap.
  Files: `game/sim/Keyrun.Sim/Encounter/Rewards.cs`, `game/sim/Keyrun.Sim/Run/Ascension.cs`,
  `game/sim/Keyrun.Sim.Tests/Encounter/RewardsTests.cs`.
- **FORK-PARITY** (bible 6): the bots-suite dominance check over every fork. Files:
  `game/bots/Keyrun.Bots.Tests/ForkParityTests.cs` (new).
- **KICK-READY-READOUT** (bible 10): a Q ready pip and arc on each party frame. Files: `game/src/Hud/PartyFrames.cs`,
  `game/src/Hud/KickReadySceneTest.ps1` (new).
- **VOTE-TO-LEAVE** (bible 10): a party vote to abandon keeps everyone's key. Files:
  `game/sim/Keyrun.Sim/Run/Backfill.cs`, `game/sim/Keyrun.Sim/Run/ProgressionSave.cs`,
  `game/sim/Keyrun.Sim.Tests/VoteToLeaveTests.cs` (new).
- **QUEUE-SCREEN** (bible 10): the real wait, the readiness line, the 10-second ready-check timeout with no penalty,
  and the private last-3-runs strip. Files: `game/src/Hud/Queue.cs`, `game/src/Hud/Screen.cs`,
  `game/src/Hud/ScreenSceneTest.ps1`. Wait for #229 (Queue.cs) and #266/#268 (Screen.cs).
