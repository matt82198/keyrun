/**
 * sim/combat/clause-hooks.ts — the per-tick side channel that lets sim/combat's owned files
 * (casting.ts, damage.ts, payoff.ts) read the active run's Clause (design/BIBLE.md sec 8,
 * sim/encounter/clauses.ts's `EncounterRules`) without sim/combat/rules.ts having to thread it
 * through `CastContext`/`WindowsContext` itself. rules.ts (and sim/run-wiring.ts's
 * `rulesConfigOf`, which builds its `RulesConfig`) are outside this lane's edit scope (the P0
 * clause-mechanics fix); instead the composition root (sim/index.ts's `stepRun`) calls
 * `setActiveEncounterRules(run.encounter.rules)` once per tick, immediately before it hands the
 * `StepRules` closure built from `makeRules` to `sim/core/tick.ts`'s `step()` -- the same
 * "configure a module-scoped registry right before the synchronous tick runs" convention
 * sim/combat/rules.ts's own `configureAffinityEffects(cfg.effects)` already uses for effects
 * data (sim/combat/affinity.ts), just driven from one hop further out (sim/index.ts, not
 * rules.ts itself).
 *
 * SAFE BECAUSE SINGLE-THREADED & SYNCHRONOUS: `stepRun` -> `coreStep` -> the `StepRules` closure
 * -> casting.ts/damage.ts/payoff.ts all run synchronously in one JS turn with no `await` in
 * between, so a module-scoped value set immediately before that call is read only by the calls it
 * was set for. Two rooms' runs never interleave mid-tick (Node's event loop cannot preempt a
 * synchronous call stack), so this does not need to be per-run state to stay correct.
 *
 * COMBO FLAG: `runAsCombo` brackets exactly the synchronous span where a Payoff's combo effects
 * apply (payoff.ts's two call sites of sim/combat/combos.ts's `applyComboEffects`), so
 * `isComboActive()` lets damage.ts's `applyDamage` distinguish a Pact combo hit from a solo hit
 * without payoff.ts having to pass a flag through combos.ts's own (unowned) `applyComboEffects`
 * signature. Depth-counted rather than boolean so a combo effect that somehow re-enters this span
 * (none do today) fails closed instead of dropping the flag early.
 *
 * Usage: `setActiveEncounterRules(run.encounter.rules);` (sim/index.ts, once per tick)
 *        `const rules = getActiveEncounterRules();` (casting.ts, damage.ts)
 *        `runAsCombo(() => applyComboEffects(...));` (payoff.ts)
 */
import type { EncounterRules } from "../encounter/clauses";

let activeRules: EncounterRules | undefined;
let comboDepth = 0;

export function setActiveEncounterRules(rules: EncounterRules | undefined): void {
  activeRules = rules;
}

export function getActiveEncounterRules(): EncounterRules | undefined {
  return activeRules;
}

export function isComboActive(): boolean {
  return comboDepth > 0;
}

export function runAsCombo<T>(fn: () => T): T {
  comboDepth += 1;
  try {
    return fn();
  } finally {
    comboDepth -= 1;
  }
}
