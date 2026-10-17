/**
 * sim/combat/clause-hooks.ts — the per-tick side channel that lets sim/combat's owned files
 * (casting.ts, damage.ts, payoff.ts) read the active run's Clause (design/BIBLE.md sec 8,
 * sim/encounter/clauses.ts's `EncounterRules`) without sim/combat/rules.ts having to thread it
 * through `CastContext`/`WindowsContext` itself. rules.ts (and sim/run-wiring.ts's
 * `rulesConfigOf`, which builds its `RulesConfig`) are outside this lane's edit scope (the P0
 * clause-mechanics fix); instead the composition root (sim/index.ts's `stepRun`) calls
 * `withEncounterRules(run.encounter.rules, ...)` once per tick around its `sim/core/tick.ts`
 * `step()` call with the `StepRules` closure built from `makeRules`.
 *
 * SCOPED, NOT SET-AND-FORGET (fix/sim-module-state-per-run): `stepRun` wraps its `coreStep` call
 * in `withEncounterRules(run.encounter.rules, ...)`, which installs the rules for exactly that
 * synchronous span and restores the previous value on exit (even on a throw). `stepRun` ->
 * `coreStep` -> the `StepRules` closure -> casting.ts/damage.ts/payoff.ts all run in one JS turn
 * with no `await`, so two rooms' runs never interleave mid-tick. Before this, stepRun's bare
 * `setActiveEncounterRules` left the LAST stepped room's Clause live process-wide, so damage resolved
 * outside a stepRun (makeRules + core step, as several suites and bots drive) took that room's Pact
 * weighting (tests/sim/integration/module-state-per-run.test.ts). `setActiveEncounterRules` stays
 * for unit tests that drive casting.ts directly and reset it themselves.
 *
 * COMBO FLAG: `runAsCombo` brackets exactly the synchronous span where a Payoff's combo effects
 * apply (payoff.ts's two call sites of sim/combat/combos.ts's `applyComboEffects`), so
 * `isComboActive()` lets damage.ts's `applyDamage` distinguish a Pact combo hit from a solo hit
 * without payoff.ts having to pass a flag through combos.ts's own (unowned) `applyComboEffects`
 * signature. Depth-counted rather than boolean so a combo effect that somehow re-enters this span
 * (none do today) fails closed instead of dropping the flag early.
 *
 * Usage: `withEncounterRules(run.encounter.rules, () => coreStep(...));` (sim/index.ts, per tick)
 *        `const rules = getActiveEncounterRules();` (casting.ts, damage.ts)
 *        `runAsCombo(() => applyComboEffects(...));` (payoff.ts)
 */
import type { EncounterRules } from "../encounter/clauses";

let activeRules: EncounterRules | undefined;
let comboDepth = 0;

export function setActiveEncounterRules(rules: EncounterRules | undefined): void {
  activeRules = rules;
}

/** Runs `fn` with `rules` active, restoring whatever was active before on exit (even on a throw). */
export function withEncounterRules<T>(rules: EncounterRules | undefined, fn: () => T): T {
  const previous = activeRules;
  activeRules = rules;
  try {
    return fn();
  } finally {
    activeRules = previous;
  }
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
