# sim/ — the deterministic rules engine

**Purpose**: a headless, deterministic simulation of a Keyrun run. Given a seed, content definitions, and a stream
of per-tick commands from players and bots, it produces identical state on every machine. The client renders it;
the server (later) runs it authoritatively; tests replay it.

## Hard rules
- **Fixed tick: 20 Hz (50 ms).** All durations are integer ticks. No wall-clock anywhere in `sim/`.
- **Integer math only.** Positions in millimetres (int), angles in 1/1024 turns (int), damage and health as ints,
  percentages as basis points (int, 10000 = 100%). No `Math.random`, no floats in state. `sim/core/fixed.ts` is
  the only place that may touch floating point (for sqrt via integer Newton or lookup) and must be tested for
  determinism.
- **Seeded PRNG** (`sim/core/prng.ts`, xoshiro128** or mulberry32) is the only randomness; it is part of state.
- **Commands in, events out.** `step(state, commandsThisTick) -> {state, events}`. Commands are plain data
  (`{tick, actorId, kind, ...}`); events are plain data (damage dealt, cast started/interrupted, buff applied,
  death, phase change). State is a plain serialisable object; `clone(state)` and `hash(state)` exist.
- **Replays are golden tests.** A replay = seed + content version + command list. `tests/sim/golden/*.json`
  store the final state hash; any change to a hash is a deliberate content change or a bug.
- **Flat 3D arena.** Positions are 3D (x, y=up, z) on flat ground for the POC; movement is capsule-vs-cylinder
  and capsule-vs-AABB resolution; facing is a yaw; frontal cones and cleaves use yaw. No navmesh. Line of sight
  is optional and off by default.

## Layout (each file ≤190 LOC, one purpose, usage docstring at top)
- `sim/core/` — `fixed.ts` (int vector math), `prng.ts`, `tick.ts` (step loop), `commands.ts` (schema + validation),
  `events.ts`, `state.ts` (types, clone, hash), `replay.ts` (record/apply).
- `sim/combat/` — design-agnostic mechanics driven by data: `actors.ts` (health, resources, position, facing,
  role tags), `abilities.ts` (ability definition schema: cost, cooldown, cast time, GCD, range, shape
  [target/cone/circle/self], effects list), `casting.ts` (cast bars, interrupt resolution: the interrupt lands if
  it is processed in a tick strictly before the cast completes; ties on the same tick resolve by command order,
  which is deterministic), `cooldowns.ts` (GCD + per-ability, charges), `effects.ts` (buffs/debuffs with duration,
  stacks, periodic ticks, modifiers as basis points), `threat.ts`, `damage.ts` (damage pipeline: base → modifiers
  → mitigation → absorb → health, all ints), `movement.ts`, `targeting.ts` (tab-target and shape queries).
- `sim/encounter/` (added with content): enemy AI scripts as data-driven state machines, boss phases, telegraphs
  as events with a tick horizon (so the client can draw them ahead of time).

## Contracts other domains rely on
- `content/` supplies ability, class, enemy, and dungeon definitions as JSON validated by `sim/combat/abilities.ts`
  schemas; `sim` never imports `content` — content is passed in.
- `client/` and `bots/` consume `state` snapshots and produce `commands`; they never mutate state.
- `server/` (phase 4) runs `step` authoritatively and broadcasts snapshots/events; identical code.
- Walk speed and chase margin are ACTOR state (`ActorState.speedMmPerTick`/`chaseMarginMm`, content
  `EnemyDef` -> `PullSpawn` -> spawn), defaulting to `RulesConfig.baseSpeedMmPerTick`/`CHASE_MARGIN_MM`.
  Never put speed on a `Command`: core/tick.ts revalidates every command and `validateCommand` rejects one.

## Tests (Vitest, `tests/sim/`)
- Determinism: same seed + commands → same hash on 3 runs and after clone.
- Every combat rule has a unit test with integer expectations (no floats, no tolerances).
- Property test: random command streams never produce NaN, negative health below 0, or a cast that both
  completed and was interrupted.
- No module-level mutable counters: ids minted during a run live in run state (e.g. `EncounterState.nextTelegraphId`),
  so two runs in one process (two server rooms) stay seed-deterministic (`tests/sim/integration/telegraph-ids-per-run.test.ts`).
