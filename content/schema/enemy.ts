/**
 * content/schema/enemy.ts
 * `EnemyDef` has no sim equivalent -- sim/encounter's PullSpawn only carries {actorId, classId,
 * pos, yaw, health} at spawn time, and AI behaviour lives in a separate `EnemyScript` (a plain
 * `ScriptStep[]` keyed by actorId, sim/encounter/scripts.ts) that content builds from this def's
 * `script` field. EnemyDef therefore stays content-only, but `script` now holds the REAL
 * `ScriptStep`/`EnemyScript` type re-exported below instead of a local duplicate.
 *
 * Two pre-integration content-only additions have been dropped since the real Windows/Combos and
 * sim/encounter landed with a different (better) mechanism for the same rule:
 *   - `WindowRuleDef`/`ScriptStep.windowRule` (The Lock's alternation rule, BIBLE.md Sec 9) is now
 *     expressed on the ability itself: sim/combat/abilities.ts's "window" EffectOp carries its own
 *     `alternate?: boolean`, and sim/combat/windows.ts fully implements the alternation-denial and
 *     3-fail enrage counter from that flag alone. `hollowWarden.lock`
 *     (content/data/abilities/enemies.json) sets `alternate: true` directly; no script-level rule
 *     needed. `canPayoffCloseWindow` below is kept as the same pure function content's tests
 *     already exercised against real inputs, now documented as *not* sim's actual enforcement path
 *     (that's windows.ts's `tryClosePayoff`) but a content-side restatement for design review.
 *   - `ScriptAt` no longer accepts `"onDeath"`: the real `sim/encounter/scripts.ts` ScriptStep.at
 *     is `"loop" | number` only, with no death-triggered trigger kind. The Seed's "cast
 *     seed.deathBloom on death" mechanic (content/data/enemies/seed.json) cannot be expressed
 *     today -- the `seed.deathBloom` AbilityDef is kept as valid, unscripted data, and the gap is
 *     flagged as a BACKLOG item in this lane's final report (sim/encounter would need an
 *     onDeath/onSpawnDead trigger kind, or a caller-side hook in sim/index.ts's stepRun).
 *
 * `breakBarId`/`breakBars` (Sundered Twins, BIBLE.md Sec 9) are kept content-only: the *type* they
 * need already exists (sim/encounter/breakbar.ts's BreakBarState/BreakBarInput/drainBreakBar), but
 * nothing in sim/index.ts's stepRun or sim/encounter/index.ts's stepEncounter yet builds a
 * BreakBarState from content data or feeds interrupt/control-tagged commands into drainBreakBar
 * per tick -- also flagged as a BACKLOG item, not fixed here.
 */
export type { ScriptStep, EnemyScript, TelegraphSpec } from "../../sim/encounter/scripts";

import type { EffectOp } from "./ability";
import type { EnemyScript } from "../../sim/encounter/scripts";

export type EnemyRole = "melee" | "caster" | "lieutenant" | "elite" | "boss";

/** ADDED: a Break bar shared by two or more enemies (Sundered Twins, BIBLE.md Sec 9). Any
 * interrupt- or control-tagged ability drains it; at 0, `onEmpty` effects fire on both adds. See
 * the file docstring -- sim has the mechanism (sim/encounter/breakbar.ts) but no content wiring
 * yet. */
export interface BreakBarDef {
  id: string;
  max: number;
  drainPerInterrupt: number;
  drainPerControl: number;
  onEmpty: EffectOp[];
}

export interface EnemyDef {
  id: string;
  name: string;
  maxHealth: number;
  role: EnemyRole;
  abilities: string[];
  script: EnemyScript;
  /** Walk speed in mm/tick (sim ActorState.speedMmPerTick); absent = the run's baseSpeedMmPerTick. */
  speedMmPerTick?: number;
  /** Chase stop margin inside engage range, mm (sim ActorState.chaseMarginMm); absent = CHASE_MARGIN_MM. */
  chaseMarginMm?: number;
  /** ADDED: id of a BreakBarDef (declared on the encounter's boss entry) this enemy shares. */
  breakBarId?: string;
  /** ADDED: only populated on the encounter's primary boss entry; adds reference it by id. */
  breakBars?: BreakBarDef[];
}

/**
 * BIBLE.md Sec 9 "The Lock" restated as a content-side pure function for tests/design review. The
 * actual runtime enforcement is sim/combat/windows.ts's `tryClosePayoff`, driven entirely by the
 * `alternate: true` flag on the boss's own "window" EffectOp (see file docstring) -- this function
 * is not called from sim.
 */
export function canPayoffCloseWindow(payoffClass: string, lastSetupClass: string | null): boolean {
  return lastSetupClass === null || payoffClass !== lastSetupClass;
}
