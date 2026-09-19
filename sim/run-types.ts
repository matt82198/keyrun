/**
 * sim/run-types.ts — the public shapes `sim/index.ts`'s `createRun`/`stepRun` and `sim/run-wiring.ts`
 * both need (split out purely to keep sim/index.ts under sim/CLAUDE.md's <=190-LOC-per-file cap --
 * no behaviour lives here, only types). `Run`/`RunContent`/`PartyMemberSpec`/`StepRunOpts` used to
 * be declared directly in sim/index.ts; every field and doc comment is unchanged, just relocated.
 *
 * Usage: `import type { Run, RunContent, PartyMemberSpec, StepRunOpts } from "./run-types";`
 */
import type { ActorId, Tick, Vec3 } from "./core/state";
import type { SimState } from "./core/state";
import type { ComboTable } from "./combat/combos";
import type { AbilityDef } from "./combat/abilities";
import type { EffectDef } from "./combat/effects";
import type { Bounds } from "./combat/movement";
import type { BreakBarSource, DungeonDef, EncounterState, EnemyScript } from "./encounter/index";
import type { EncounterRules } from "./encounter/clauses";
import type { RunEndState } from "./run-wiring";

/** One party member to spawn as a player actor at run creation. */
export interface PartyMemberSpec {
  id: ActorId;
  classId: string;
  role: "tank" | "healer" | "damage" | "none";
  pos: Vec3;
  yaw?: number;
  maxHealth: number;
  maxResource: number;
}

/**
 * The data a run needs beyond the party and dungeon: ability/effect definitions for
 * `sim/combat/rules.ts` (see RulesConfig), the enemy AI scripts for `sim/encounter`, and the
 * encounter pacing knobs (`keyLevel`/`parTicks`) that seed the initial EncounterState. `content`
 * never comes from a `content/` package import -- per sim/CLAUDE.md, sim never imports content;
 * callers (client, tests) pass their own definitions in.
 */
export interface RunContent {
  abilities: Record<string, AbilityDef>;
  effects: Record<string, EffectDef>;
  lockoutTicks: Tick;
  baseSpeedMmPerTick: number;
  actorRadiusMm: number;
  bounds: Bounds;
  enemyScripts: Record<ActorId, EnemyScript>;
  keyLevel?: number;
  parTicks?: number;
  encounterRules?: EncounterRules;
  /** [setupClass][payoffClass] -> ComboDef for target/self ("burst") Windows and Combos
   * (design/BIBLE.md §4, §5); passed straight through to `sim/combat/rules.ts`'s `RulesConfig`.
   * Omit for content that does not use Windows and Combos yet -- window/payoff ops then simply
   * find no combo and no-op (see RulesConfig's own docstring). */
  combos?: ComboTable;
  /** Same shape as `combos`, for the smaller "field" combos floor-patch Windows fire. */
  fieldCombos?: ComboTable;
  /** [barId] -> break-bar source data (break-bar-wiring, BIBLE.md Sec 9 "The Sundered Twins"),
   * passed to `stepEncounter` as `BreakBarWiring.sources` on every tick. Omit for content with no
   * break bars -- `stepRun` then drains none. */
  breakBarSources?: Record<string, BreakBarSource>;
}

export interface Run {
  state: SimState;
  encounter: EncounterState;
  dungeon: DungeonDef;
  content: RunContent;
  /** P2 policy-inside-stepRun: the run's own terminal state (result + the tick it ended), threaded
   * by stepRun itself every tick -- see run-wiring.ts's checkRunEnd. Never null once created (a
   * fresh run's own `result` starts null, not the field itself). */
  runEnd: RunEndState;
}

/** Options a host may pass to `stepRun`, for the one run-end signal sim cannot derive on its own --
 * see run-wiring.ts's file docstring. */
export interface StepRunOpts {
  abandonRequested?: boolean;
}
