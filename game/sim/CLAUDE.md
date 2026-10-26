# game/sim/ - Keyrun.Sim map (C# rules engine)

Navigation only. The engine is the pure net8.0 integer sim: no Godot reference, no floats, no clocks. Parity contract and
trace format: `ORACLE.md` (this folder). Purity rules and banned symbols: `Keyrun.Sim/BannedSymbols.txt` and
`BannedSymbols.Sim.txt`. Game-wide rules: `game/CLAUDE.md`. Domain map: root `CLAUDE.md`.

## Folder rows (path, what it owns, test project)

| Path (under game/sim/) | Owns | Tests |
|---|---|---|
| `Keyrun.Sim/Core/` | Integer fixed-point and math (Fixed, IntMath, Atan, Vec3), Prng, Tick, State, Commands and their JSON, Events, Replay, Fnv1a hashing, Projection | `Keyrun.Sim.Tests/Core/`, `CoreStepTests.cs` |
| `Keyrun.Sim/Combat/` | Casting (`Casting.*.cs`: Start, Target, Haste, Complete, End, Stop, Overheat), Holder and HolderChangeLog (`Holder.cs`), Threat (`Threat.cs`), Damage, Mitigation, Effects, Cooldowns, Targeting, Windows, Combos, Movement, Riposte | `Keyrun.Sim.Tests/Combat/`; most combat tests at `Keyrun.Sim.Tests/` root (`Combat*Tests.cs`, `HolderLockTests.cs`) |
| `Keyrun.Sim/Encounter/` | Enemy and boss rules: Pulls, Scripts, Chase (`Chase.cs`), Leash (`Leash.cs`), Aggro, EnemyAggro, Patrols, IdleWander, Telegraphs, Boss, BreakBar, Forces, Tempo, Timer, Rewards, SeedLoot, EncounterStep | `Keyrun.Sim.Tests/Encounter/` (62 files) |
| `Keyrun.Sim/Nav/` | R-PATH-1 deterministic pathing: NavGrid (A* FindPath), NavFollow, CastSight | No `Nav/` test folder. Layout tests: `Keyrun.Sim.Tests/NavLayoutRunTests.cs`, `WalkableTests.cs`, `Encounter/` |
| `Keyrun.Sim/Dungeon/` | Seeded dungeon generator: RoomGraph, RoomGraphGen, WalkGrid, WalkGridGen, PackSlots, PackFiller, BranchFill, SegmentFill, DungeonPipeline | `Keyrun.Sim.Tests/Dungeon/` |
| `Keyrun.Sim/Run/` | Run root: RunTypes, Run, RunStart, RunPipeline, RunWiring, Backfill, CauseRows, EndOfRun, Ascension, Ash, tree and gear state, CompletionRoll | `Keyrun.Sim.Tests/Run/` |
| `Keyrun.Sim/Run/Phases/` | One step phase per file (CombatPhase, PullsOrLayoutPhase, DeathsAndWipePhase, RunEndPhase, ...); referenced from `Run.cs` and `RunPipeline.cs` | Covered through `Keyrun.Sim.Tests/Run/` and root run tests |
| `Keyrun.Sim/SimInfo.cs` | `SimInfo.Name`, the assembly identity the boot scene reads for the headless smoke | none of its own |
| `Keyrun.Sim.Tests/` | xUnit suite (138 root-level test files plus the subfolders above); `Fixtures/` holds `boss/` and `oracle/` data; `Host/` holds affix-week and replay-frame tests | itself |

## Rules for editing here

- Integers only in `Keyrun.Sim` and `Keyrun.Content`; signed division goes through `IntMath.FloorDiv` or `IntMath.TruncDiv`.
- The oracle fixtures under `Keyrun.Sim.Tests/Fixtures/oracle/` are recordings of this sim (STATE.md D28, amended by
  R-ORACLE-1). Regenerating them needs a stated reason next to the new value.
- Behaviour rules that have no TypeScript oracle are named in `game/CLAUDE.md` (its section for each rule), not here.
- Engine launches are orchestrator-only (see `game/CLAUDE.md`). Lanes run `dotnet test game/Keyrun.sln` or
  `tools/fastcheck.ps1`.

## Where else to look

- Files that have no row in game/CLAUDE.md, including the Nav/ and Encounter/ files: `game/CLAUDE.md`, section "Files with no row".
- Dungeon generator plan of record: `game/design/ARCHITECTURE.md` (STATE.md D63).
