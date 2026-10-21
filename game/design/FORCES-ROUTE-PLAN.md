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

Parallel waves (from the dependencies): A = 0, 2, M1, M2, M4, M5. A2 = 1 (after 0 and M4), 5. B = 3, 6. M3 follows 0 and KICK-READY-READOUT. C = 4, 12. D = 7, 9. E = 8, 10. F = 11.

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

## M1. TIMER-TIERS: Mythic+ timer, upgrade tiers and Slack as grace (bible 8)

- **Files:** `game/sim/Keyrun.Sim/Encounter/Timer.cs` (`FinishRun`: timed when the finish tick is no later than par
  plus remaining Slack; upgrade +1, +2 within 8000 bp of par, +3 within 6000 bp), `game/sim/Keyrun.Sim/Encounter/RunEndTitle.cs`,
  `game/sim/Keyrun.Sim/Run/ProgressionSave.cs` (the key level moves by the tier), `game/src/Hud/TimerSlack.cs` (+2
  and +3 marks on the timer bar), `game/sim/Keyrun.Sim.Tests/Encounter/TimerTests.cs`,
  `game/sim/Keyrun.Sim.Tests/Encounter/UpgradeTierTests.cs` (new).
- **Red test:** a run with no deaths that finishes 1 tick past par plus its initial Slack is depleted, while today it
  reads timed. One death on a run finishing at par is still timed (test 3). Finishes at exactly 6000, 8000 and
  10000 bp of par upgrade by +3, +2 and +1. A depleted run does not upgrade, and its key stays at the same level (Matt,
  2026-10-20: no drop).
- **Depends on:** none (no file shared with 0).
- **Wait for:** #253 (edits `game/src/Hud/TimerSlack.cs` and adds RunClock), plus #283 and #274 if the oracle
  digest moves (D28).

## M2. STOPS: control ends an interruptible cast (bible 9.3)

- **Files:** `game/sim/Keyrun.Sim/Combat/Casting.cs` (a stun, freeze or silence applied during an interruptible cast
  ends it with an `interrupt {by: "stop"}` event), `game/sim/Keyrun.Sim/Combat/ClauseHooks.cs` (Echo spawns on a stop),
  `game/sim/Keyrun.Sim.Tests/CombatStopTests.cs` (new).
- **Red test:** Shatter's stun, Cold Snap's freeze and Ward Dissonance's silence each end a live Choral Mending, and
  each is a no-op on a cast that is not interruptible. Under Echo a stop spawns the add. A stop does not refund
  Quench's Heat.
- **Depends on:** none.
- **Wait for:** #283 (edits `Casting.cs`; the Quench refund rule lands there).

## M3. KICK-ORDER-CASTBAR: the suggested kick order and the shielded cast bar (bible 9.3)

- **Files:** `game/content/Keyrun.Content/Model/KickOrder.cs` (new, Godot-free: order the casters by which party
  kick comes off cooldown first), `game/src/View/CastBarView.cs` (shield edge on casts that cannot be interrupted),
  `game/src/View/LabelView.cs` (the order number on the nameplate), `game/content/Keyrun.Content.Tests/KickOrderTests.cs`
  (new), `game/src/Host/CombatViewSceneTest.ps1`.
- **Red test:** with kicks ready at ticks 0, 100 and 300 and three casters engaged, the order is 1, 2, 3 by cooldown
  and by caster id on ties. A non-interruptible cast's bar shows the shield edge and an interruptible one does not.
- **Depends on:** 0 (engage tick), and the next-increments KICK-READY-READOUT (party-frame cooldown model).
- **Wait for:** #263 (edits `game/src/View/LabelView.cs`).

## M4. TWINS-DIE-WITH-BOSS: the boss's death ends the encounter (bible 9.6)

- **Files:**
  - `game/sim/Keyrun.Sim/Encounter/Boss.cs` and `game/sim/Keyrun.Sim/Encounter/EncounterStep.cs`: on the boss's death
    tick, every living enemy of the boss encounter (the Twins, plus Echo adds) despawns with one
    `despawn {actorIds}` event, before run_end.
  - `game/sim/Keyrun.Sim.Tests/Encounter/BossDeathDespawnTests.cs` (new).
  - `game/sim/Keyrun.Sim.Tests/Fixtures/boss/hollow-warden-boss.trace.jsonl` (the boss golden, regenerated per D28 with
    reason "Twins die with the boss").
- **Red test:** a boss killed while both Twins live leaves no living enemy on its death tick, emits one `despawn`
  naming both Twins, and run_end follows on the same tick. A Twin killed earlier is not in the event. The
  TWIN-AFTER-RUNEND freeze still holds.
- **Depends on:** none. It must land before 1, which also edits `EncounterStep.cs`.
- **Wait for:** #283 (rewrites the same boss golden and the oracle traces) and #274 (manifest). This is the trace
  chokepoint, so the golden is regenerated once, after both.

## M5. FORTIFIED-TYRANNICAL: the key's affixes (bible 8)

- **Files:**
  - `game/sim/Keyrun.Sim/Encounter/Affixes.cs` (new): `AffixesFor(keyLevel, rotationIndex)`. Keys 2-4 get none, keys
    5-9 get Fortified or Tyrannical by rotation parity, key 10 and up get both. Fortified is +2000 bp health and
    +3000 bp damage on non-boss enemies. Tyrannical is +2500 bp health and +1500 bp damage on the boss and its adds.
  - `game/sim/Keyrun.Sim/Encounter/Tempo.cs`: `UnlockedMechanicCount` (keys 5/10/15) is superseded, and its only
    consumer is `TempoTests`.
  - `game/sim/Keyrun.Sim/Run/RunOptions.cs` (rotation index), `game/sim/Keyrun.Sim/Run/Clause.cs` (the Clause
    rotation layers on top, with its starting key tuned).
  - `game/src/Host/HostRunSetup.cs` (apply health at spawn, ~456 and ~472), `game/src/Hud/Screen.cs` (show the key's
    affixes and Clause).
  - `game/sim/Keyrun.Sim.Tests/Encounter/AffixesTests.cs` (new) and `game/sim/Keyrun.Sim.Tests/Encounter/TempoTests.cs`.
- **Red test:** keys 4, 5 and 10, each with rotation indices 0 and 1, give none; Fortified then Tyrannical; and both,
  both. A key-10 trash spawn has 12000 bp health and its hits 13000 bp damage, and a key-10 boss has 12500 bp and
  11500 bp. The Clause stays exactly one at a time on top.
- **Depends on:** none.
- **Wait for:** #233 (edits `HostRunSetup.cs`), #266 and #268 (`Screen.cs`), and #283 and #274 (the key-10 oracle
  trace `bots-hv-k10-s7` moves, regenerated per D28).

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
- **Depends on:** 0 (prespawned groups; EncounterTypes.cs and EncounterState.cs), M4 (EncounterStep.cs).
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
  never moves for a pack. Pack kills credit forces through increment 1. A patrol pack (`PackDef.patrol`, a closed
  loop of points at a fixed mm per tick) is at the same position on the same tick in every run, and its ring
  engages from wherever it has walked to (bible 9.1, Patrols).
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
  door, with a seal naming an unknown box, or with a branch that has no stair to the Seed chamber. There is no
  walking-distance check: walking between packs is part of the route decision, and the stakes come from the timer
  and the forces bar (bible 9.1, "Zero downtime means stakes, not spawns").
- **Depends on:** 1, 2 (ContentLoader.cs), 3 (Run.cs).
- **Wait for:** none.

## 5. CASTER-OPS-SIM: the effect ops the new casters need

- **Files:** `game/sim/Keyrun.Sim/Combat/Effects.cs` (Stoked damage-up aura, Drenched slow, a dispellable magic burn
  if Purge and Cleanse cannot already remove it), `game/sim/Keyrun.Sim/Combat/Movement.cs` (Undertow pull toward the
  caster, walkable-clamped, 20-tick root), `game/sim/Keyrun.Sim/Combat/Casting.cs` (Stoked +20% cast speed),
  `game/content/Keyrun.Content/Model/EffectOps.cs`, `game/sim/Keyrun.Sim.Tests/CasterOpsTests.cs` (new).
- **Red test:** Undertow moves its target 5000 mm toward the caster, stopping at a wall, and roots it for 20 ticks.
  Stoked raises an enemy's damage by 3000 bp and shortens its casts by 2000 bp for 160 ticks, then ends. Searing Hex's
  burn is removed by Cinder's Purge and by Chanter's Cleanse. Drenched slows movement by 3000 bp for 60 ticks. Cinder's
  Purge cast on a Stoked enemy removes Stoked (the Mythic+ soothe/purge, bible 9.3).
- **Depends on:** M2 (both edit `Casting.cs`).
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
  cone telegraph, and the existing DangerousCastTelegraphTests pass over the new files. The Ash Hexer has an onDeath
  step that leaves Cinders (a 2000 mm circle telegraphed 40 ticks ahead, burning for 120 ticks). W1's pack entry
  carries its patrol loop on the Undercroft Steps.
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
- **LOOT-BY-KEY** (bible 7): the tiers offered rise with the key level the run was played at. Files:
  `game/sim/Keyrun.Sim/Encounter/Rewards.cs`, `game/sim/Keyrun.Sim.Tests/Encounter/RewardsTests.cs`. It shares
  Rewards.cs with REWARD-GUARANTEE and 12, so it lands in sequence with them.
- **PRIVATE-RATING** (bible 10): a WoW-style Mythic+ rating, shown only on the player's own screen and never in
  matching. Files: `game/sim/Keyrun.Sim/Run/Rating.cs` (new), `game/sim/Keyrun.Sim/Run/ProgressionSave.cs`,
  `game/src/Hud/Screen.cs`, `game/sim/Keyrun.Sim.Tests/RatingTests.cs` (new), and a test that no queue or match input
  reads the rating.
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

## 2026-10-21: building the bible 9.2 packs (W1, W2, E1-E3) on today's main

Finding f-82d9390e (Matt 2026-10-20: "room design is lame, 2 mobs 4 mobs 2 bosses, not following the bible"). PR #533
(open) pinned the three teaching pulls and the boss to bible s9 and found them already matching; the gap it left
is the pack layer. This section re-plans increments 3, 4, 6, 7, 10 and 11 above against main at b8e2d5d9, because
two things changed under them: packs.json is retired and packs live only in layout.json (ruling R7, #513), and
STATE.md D44 removed every gate. The sections above stay as the 2026-10-20 record. Where they conflict, this
section is the newer plan, except where it waits on a decision listed at the end.

**What already landed.** #286 (MERGED 2026-10-20, 704c6af8) wrote bible s9 and this plan. #288 (MERGED, 9cc91ec8)
added the forces table, the route shape and the hazards schema. #302 (d2f1df73) added stops (M2) and the caster ops
(increment 5). #349 added the Forces bar, #381 the skippable packs, #502 Hall Sentries (MVP-U8), and #511, #517 and
#513 the pack `branch` field, the per-branch totals and the single pack layer.

**D44, verbatim (STATE.md:1531-1533):** "no more gates, just path to boss with packs as you said, make the rest of the
decisions and we will tune later" and "just build the game". D44 (2026-10-21) is newer than bible 9.1's branch seal
and 9.5's Hollow Door (2026-10-20). This plan does not build either one. Both are open decisions 2 and 3 below.

### (1) Bible 9.2 packs against main

Sources: bible 9.2 table (design/BIBLE.md:417-426), the casts (:449-459), Cinders (:467-468), the W1 patrol (:346-348)
and the hazards (:490-502). "Main" means b8e2d5d9.

| Pack (room) | Members (pts each) | Casts and hazards | Pts | On main today |
|---|---|---|---|---|
| W1 Hound Kennel (Undercroft Steps, patrols) | 4 Hollow Hound (35), 1 Ash Hexer (80) | Searing Hex (Hexer), Cinders on Hexer death, hound melee | 220 | No enemy, ability or forces entry. Patrol: no (`Aggro.GroupsOf` discards `tick`, Aggro.cs:48) |
| W2 Bellows Crew (Bellows Hall) | 2 Vault Brute (90), 2 Bellows Keeper (70), 1 Ash Hexer (80) | Crushing Slam, Stoke (Stoked), Searing Hex, Cinders; hazard **Vent Gout** | 400 | No enemies. Vent Gout: schema only (`HazardDef` id/name/room, Hazards.cs:7-17; hazards.json is `[]`), no sim |
| E1 Reading Watch (Reading Gallery) | 2 Choir Acolyte (40), 1 Drowned Chorister (60) | Undertow | 140 | Acolyte yes (`choir.melee`, forces.json:4). Chorister no |
| E2 Loft Choir (Cantor's Loft alcove) | 1 Acolyte (40), 1 Choir Cantor (80), 1 Chorister (60) | Choral Mending, Undertow | 180 | Cantor and Choral Mending yes (`choir.healCast`, content/data/abilities/enemies.json:28-29). Chorister no |
| E3 Floodgate Watch (Sluice Gallery) | 1 Brute (90), 1 Acolyte (40), 1 Chorister (60), 1 Keeper (70) | Crushing Slam, Undertow, Stoke; hazard **Floodjet** (Drenched) | 260 | Brute, Chorister and Keeper no. Floodjet: schema only, no sim |

Mechanisms that exist without content: the Searing Hex burn (`DamagePctOp`, EffectOps.cs:20-31), the Undertow pull
(`PullOp`, :33-41), Stoked, Drenched and Purge on Stoked (sim, #302, `game/sim/Keyrun.Sim.Tests/CasterOpsTests.cs:48-149`
on test-local defs), on-death casts (`"at": "onDeath"`, content/data/enemies/seed.json:14), cone telegraphs
(content/data/abilities/enemies.json:283) and stops (#302). Missing on main: the Fork landing and the five branch rooms
(layout.json:4-39 is one chain), pack links (packs get `Array.Empty<string>()`, Aggro.cs:54) and patrols.

### (2) Point arithmetic

Teaching pulls: Choir 160, Pair 260, Seed 180, so 600 (bible :419-421, forces.json:3-7, critical path pinned by #533).
The bar is 1000 (forces.json:2).

| Route | Sum | Clears 1000 |
|---|---|---|
| West: W1 / W2 / W1+W2 | 820 / 1000 / 1220 | no / yes / yes |
| East: E1 / E2 / E3 | 740 / 780 / 860 | no |
| East: E1+E2 / E1+E3 / E2+E3 / all three | 920 / 1000 / 1040 / 1180 | no / yes / yes / yes |

These match bible :428-435: W2 is required on West, and on East E3 is required plus E1 or E2.

Today's layout packs (layout.json:55-81) are cantor-vigil (3 Cantor x 80 + 4 Acolyte x 40 = 400, in the choir-room),
hall-sentries (5 x 48 = 240, in the pair-hall, branch-neutral) and warrior-remnant (5 x 52 + 2 x 80 = 420, in the
seed-chamber). Today's routes reach 600+400 = 1000, 600+420 = 1020 and 600+240 = 840. The branch totals are 1240 and
1260 (RouteContentTests.cs:117-118).

**With cantor-vigil and warrior-remnant removed:**
- **Alone, with nothing else changed:** the most any route can reach is 600 + 240 = 840. LayoutValidator.cs:191-193
  fails the load ("reach 840 of the 1000"), so the removal cannot ship by itself. It must land in the same PR that
  adds the fork packs (slice S7).
- **If they are kept beside the fork packs:** 600 + cantor-vigil alone fills the bar, so the fork's route choice
  never matters. That breaks the bible :428-429 rule that every route needs a fork pack.
- **If hall-sentries (240, not a bible enemy) is also kept:** West W1+HS reaches 1060, so W2 becomes skippable. East
  E2+HS reaches 1020 and E3+HS 1100, so E3 is no longer required. The bible's arithmetic only holds with all three
  packs removed.
- **The bar decides nothing yet.** The run ends on the pull index (RunWiring.cs:368), and `Timer.IsTimed`
  (Timer.cs:163) has no forces term. Until S8 lands, 1000 is only the HUD readout.

### (3) Slice order: one lane per file domain, no file shared between lanes

| # | Lane (domain) | Owns (only these files) | Red-first test | After |
|---|---|---|---|---|
| S1 | VAULT-KITS (content data) | `game/content/data/enemies/vault.json`, `game/content/data/abilities/vault.json`, `game/content/data/effects/vault.json` (new), `game/content/Keyrun.Content/Loading/ContentLoader.cs` (registers them), `game/content/Keyrun.Content.Tests/EnemyLineTelegraphGuardTests.cs`, `game/content/Keyrun.Content.Tests/VaultKitsTests.cs` (new) | `VaultKitsTests.Every_9_3_cast_loads_with_its_bible_cast_time_cooldown_and_telegraph` (+ `Ash_Hexer_death_leaves_Cinders_2000_mm_led_by_40_ticks`) | none |
| S2 | VAULT-FORCES (content data) | `game/content/data/forces.json`, `game/content/Keyrun.Content.Tests/VaultForcesTests.cs` (new) | `VaultForcesTests.Every_9_2_enemy_carries_its_bible_points` (35/80/90/70/60) | S1 |
| S3 | PACK-SCHEMA (validator) | `game/content/Keyrun.Content/Model/Layout.cs` (pack `patrol` loop, pack `links`), `game/content/Keyrun.Content/Loading/LayoutValidator.cs`, `game/content/Keyrun.Content.Tests/PackSchemaTests.cs` (new) | `PackSchemaTests.A_patrol_waypoint_outside_its_room_fails_the_load` | none |
| S4 | SKIP-BAR (validator; restated for one path by R-PACKS-1) | `game/content/Keyrun.Content/Loading/ContentValidator.cs`, `game/content/Keyrun.Content.Tests/SkipBarTests.cs` (new) | `SkipBarTests.A_layout_without_two_disjoint_pack_sets_that_reach_the_bar_fails_the_load` (at least two disjoint minimal sets must reach 1000) | none |
| S5 | PATROL (sim rule) | `game/sim/Keyrun.Sim/Encounter/Aggro.cs`, `game/sim/Keyrun.Sim/Encounter/LayoutPacks.cs`, `game/sim/Keyrun.Sim/Encounter/Patrol.cs` (new), `game/sim/Keyrun.Sim.Tests/Encounter/PatrolTests.cs` (new) | `PatrolTests.A_patrol_ring_walks_its_loop_and_engages_where_it_stands_that_tick` | S3 |
| S6 | HAZARDS (sim rule) | `game/sim/Keyrun.Sim/Encounter/Hazards.cs` (new), `game/sim/Keyrun.Sim/Encounter/EncounterStep.cs`, `game/content/Keyrun.Content/Model/Hazards.cs`, `game/content/data/hazards.json`, `game/sim/Keyrun.Sim.Tests/Encounter/HazardsTests.cs` (new) | `HazardsTests.Vent_Gout_erupts_set_A_then_set_B_160_ticks_later_and_stops_when_W2_is_dead` | S7 (hazard rooms must exist, ContentValidator.cs:111-112) |
| S7 | PATH-LAYOUT (layout; R-PACKS-1) | `game/content/data/layout.json` (five rooms on the one path between pair-hall and seed-chamber, W1-E3 in bible order with no `branch`, W1 patrol; removes cantor-vigil, warrior-remnant and hall-sentries per R-PACKS-4/5), and re-derives `PathPackTests.cs`, `OptionalBranchForcesTests.cs`, `RouteContentTests.cs`, `SkippablePackTests.cs` (content tests), `PathPackRouteTests.cs`, `SkippablePackRouteTests.cs` (bots tests), plus `game/content/Keyrun.Content.Tests/BiblePacksLayoutTests.cs` (new) | `BiblePacksLayoutTests.Each_9_2_pack_stands_in_its_bible_room_with_its_members_and_points` | S1, S2, S3 |
| S8 | FORCES-COMPLETION (sim rule; R-PACKS-3) | `game/sim/Keyrun.Sim/Run/RunWiring.cs`, `game/sim/Keyrun.Sim/Encounter/Timer.cs`, `game/sim/Keyrun.Sim.Tests/ForcesCompletionTests.cs` (new) | `ForcesCompletionTests.A_boss_kill_at_999_points_does_not_complete_the_run_until_forces_reach_1000` | S9 |
| S9 | BOTS-ROUTE (bots) | `game/bots/Keyrun.Bots/shared/RoutePlan.cs` (new), `game/bots/Keyrun.Bots/shared/Follow.cs`, `game/bots/Keyrun.Bots/shared/Ledger.cs`, `game/bots/Keyrun.Bots.Tests/RoutePlanTests.cs` (new) | `RoutePlanTests.The_plan_pulls_a_minimal_pack_set_that_reaches_1000_and_walks_past_the_rest` | S7, S5, S6 |

**Waves (single path, per the rulings below).** Wave A runs S1, S3 and S4, then S2 once S1 lands. Wave B is S7, the
layout under R-PACKS-1. Wave C runs S5 and S6. Wave D is S9, then S8.

**Notes.**
- S7 moves the replay digest, so it re-measures it per D28 and records the reason.
- No lane edits `content/data/` (D12).
- S7 is the only lane that adds or removes a layout pack. Every pack test that pins the old three packs is
  re-derived in S7, never loosened.
- Every lane writes its own row file under `game/lanes/`, checked by `npx vitest run tests/lanes`.

### (4) Open decisions for Matt (the core loop defers to WoW Mythic+, bible :264-269)

1. **Fork or one path:** build West and East as two branches off a Fork landing (bible 9.1), or put all five packs on
   D44's one forward path. WoW default: two branches, because Mythic+ dungeons offer alternate trash routes.
2. **Branch seal:** shut the untaken branch for the rest of the run (bible :365-372, a gate under D44), or leave both
   branches open. WoW default: open, because Mythic+ never locks a route and skipped trash stays reachable.
3. **Hollow Door:** add a door that opens at 100% forces (bible 9.5, a gate D44 removed), or have no door and count the
   run complete only when the boss is dead and forces are at 1000 or more. WoW default: no door. In Mythic+ the boss
   can always be pulled, and the run completes only with the boss dead and forces at 100%.
4. **cantor-vigil and warrior-remnant:** remove them in S7 when the fork packs land, or keep them beside the fork
   packs. WoW default: remove them, because the dungeon's designed trash is the route, and keeping them lets
   600 + 400 fill the bar without a fork pack. Under R-SEED-1 (STATE.md:681, :734) cantor-vigil is a Choir-room pack
   and the warrior-remnant link was withdrawn. D44 asks for packs on the path but does not name these two, so removing
   them does not override D44.
5. **hall-sentries (`hall.sentry`, not a bible enemy):** remove it in S7, or keep it as a 240-point neutral pack.
   WoW default: remove it. Keeping it breaks bible :428-435, because W2 and E3 both stop being required.
6. **Overflow:** a run opened at 110% raises a reward tier (bible :394-397), or forces past 100% do nothing (bible
   :509-510; f-e8bddb37). WoW default: do nothing, because Mythic+ pays nothing for overflow.

### Orchestrator rulings 2026-10-21

Precedence: Matt's D44 (2026-10-21, verbatim "no more gates, just path to boss with packs", STATE.md:1532) is newer
than his 2026-10-20 "not following the bible" verdict (f-82d9390e) and is a decision, so it governs the shape. The
bible governs the packs' content (9.2 :417-426, 9.3 :449-459). WoW Mythic+ governs loop questions (bible :264-269).

- **R-PACKS-1 (decision 1): one forward path (D44), not a Fork landing.** W1, W2, E1, E2 and E3 become prespawned
  packs along that single path, in bible order (west, then east). Each keeps its bible enemies, casts and points.
  Route choice survives the way Mythic+ has it: the party picks which skippable packs to pull to reach 100%, not which
  branch to walk. **Flagged for Matt as reversible:** a fork is a later layout PR if he wants the bible 9.1 map back.
- **R-PACKS-2 (decision 2): no branch seal** (D44: no gates).
- **R-PACKS-3 (decision 3): no Hollow Door** (D44). The run completes only with the boss dead and forces at 100%
  (WoW). S8 builds this only after S9, so the bots can reach 100% first. Until then, completion stays on the pull
  count (RunWiring.cs:368).
- **R-PACKS-4 (decision 4): cantor-vigil and warrior-remnant are removed in S7, in the same PR as the new packs.** The
  LayoutValidator 1000 bar (LayoutValidator.cs:191-198) proves the route still reaches 1000.
- **R-PACKS-5 (decision 5): hall-sentries is removed** (`hall.sentry` is not a bible enemy). On one path no neutral
  pack can make W2 or E3 non-skippable, because adding a pack only adds ways to reach the bar. The numbers are below.
- **R-PACKS-6 (decision 6): overflow past 100% does nothing** (WoW; bible :509-510). The bible :394-397 overflow tier
  is not built.

**Single-path arithmetic (R-PACKS-1, 4, 5).** Teaching pulls 600, plus W1 220, W2 400, E1 140, E2 180 and E3 260.
Pulling every pack gives 1800 (180%). Every pack is optional, and the bar needs at least 400 from packs.

- **Minimal sets that reach 1000:** W2 (1000), W1+E2 (1000), E1+E3 (1000), E2+E3 (1040) and W1+E3 (1080).
- **Sets that fall short:** any one pack other than W2 (W1 820, E1 740, E2 780, E3 860), W1+E1 (960) and E1+E2 (920).
- **Validator results:**
  - The old skippable rule passes: without any single pack, 1800 - 400 = 1400 still reaches 1000 (LayoutValidator.cs:194-198).
  - The walking-line rule passes: the line alone is 600 (:202-207).
  - CheckBranchTotals has nothing left to check (no pack has a `branch`).
- **What this changes from the bible:** bible :428-435's "W2 required on West, E3 required on East" cannot hold on one
  path. Both are skippable (W1+E2 = 1000), and no neutral pack changes that.
- **S4 SKIP-BAR** keeps the route choice honest instead. It fails the load unless at least two disjoint minimal sets
  reach 1000. Today there are W2 and E1+E3, for example.
