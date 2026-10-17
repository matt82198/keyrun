/**
 * Affinity: hostile-vs-friendly classification for circle/cone/line ability shapes (see
 * sim/combat/targeting.ts's resolveTargets) -- the missing faction filter behind the P0 defect
 * where Cinder's cinderfield burns its own party and enemy cleaves hit other enemies. "hostile"
 * (damage/debuff) resolves only actors of the opposing `kind`, excluding the caster; "friendly"
 * (heal/buff) resolves only the caster's own `kind`, including the caster.
 *
 * Derived from an ability's own effect ops: "damage"/"interrupt" always signal hostile, "heal"
 * always signals friendly, and "applyEffect" defers to effects-remove.ts's `effectPolarity` for
 * the target effect (reused, not duplicated) via the registry `configureAffinityEffects` sets.
 * Ops carrying no inherent polarity ("window", "payoff", "removeEffect", "resource", "threat")
 * contribute no signal. An ability whose ops disagree (both a hostile and a friendly signal) or
 * carry no signal at all resolves hostile -- effects-remove.ts's own fail-closed default for an
 * effect it cannot classify, applied here at the ability level too, and worth naming explicitly
 * because casting.ts applies one shape-resolved target set to *all* of an ability's ops
 * (sim/combat/casting.ts is out of this lane's edit scope), so a mixed-polarity ability can only
 * ever pick one target set to resolve to.
 *
 * The registry: resolveTargets is called from casting.ts's completeCast and windows.ts's
 * applyWindowsAndCombos -- both out of this lane's edit scope -- with a fixed (state, actor, def)
 * signature that carries no effect definitions, and SimState itself carries none either
 * (sim/CLAUDE.md: content is passed in, never stored in state). configureAffinityEffects is the
 * one seam available to still reuse effectPolarity for "applyEffect" ops without touching either
 * locked file or SimState's shape. The registry is module-level and therefore process-global
 * between calls. sim/combat/rules.ts's StepRules calls configureAffinityEffects(cfg.effects) at the
 * top of every invocation (not once from makeRules), so the registry is re-set to that run's effect
 * map before any resolveTargets call made during that step. Because of this, held StepRules
 * closures from different runs are safe to step in any interleaving: each step re-points the
 * registry at its own run's effects before it resolves targets. Between calls the registry holds
 * whatever the most recent StepRules call set, so code that calls resolveTargets outside a
 * StepRules call (e.g. a unit test building a CastContext/WindowsContext directly, as
 * tests/sim/combat/targeting-resolve.test.ts does) sees that last-set map, or the fail-closed
 * hostile default for any applyEffect op it cannot look up if none was ever set. The fail-closed
 * default is already correct for every current-content ability whose only per-actor op is
 * applyEffect and is in fact hostile (Cinderfield's burning, the enemy stackCall mark); only a
 * friendly-only one (Bulwark's braced, Hymn's hymnHeal/hymnSpeed, Chanter's Ward-stance wardPulse)
 * needs the registry to classify correctly. This is content wiring, not sim state: the registry
 * carries no per-run state of its own and is never read as anything but the effects map most
 * recently set by a StepRules call.
 *
 * Usage: configureAffinityEffects(runEffects); // at the top of each StepRules call (rules.ts)
 *        const aff = abilityAffinity(def); // reads the registered effects
 *        matchesAffinity(aff, caster, candidate); // per-actor filter predicate
 */
import type { AbilityDef } from "./abilities";
import type { EffectDef } from "./effects";
import { effectPolarity } from "./effects-remove";

export type Affinity = "hostile" | "friendly";

let registeredEffects: Record<string, EffectDef> = {};

/** Registers the run's effect definitions for abilityAffinity's default lookup. See file
 * docstring for why this module-level seam exists and why it is safe for this codebase. */
export function configureAffinityEffects(effects: Record<string, EffectDef>): void {
  registeredEffects = effects;
}

/**
 * Classifies `def`'s dominant Affinity for shape-based target resolution. `effects` defaults to
 * whatever configureAffinityEffects last registered (or `{}` if never called); see file docstring
 * for the full derivation and fail-closed default.
 */
export function abilityAffinity(def: AbilityDef, effects: Record<string, EffectDef> = registeredEffects): Affinity {
  let hostile = false;
  let friendly = false;
  for (const op of def.effects) {
    if (op.op === "damage" || op.op === "interrupt") hostile = true;
    else if (op.op === "heal") friendly = true;
    else if (op.op === "applyEffect") {
      const target = effects[op.effectId];
      if (target) {
        if (effectPolarity(target) === "beneficial") friendly = true;
        else hostile = true;
      }
    }
  }
  return friendly && !hostile ? "friendly" : "hostile";
}

/**
 * True if `candidate` belongs in a shape-resolved target list of the given Affinity cast by
 * `caster`: hostile keeps the opposing `kind` (the caster's own kind always matches its own, so
 * this also excludes the caster); friendly keeps the caster's own `kind` (the caster always
 * matches, so this also includes the caster).
 */
export function matchesAffinity(
  affinity: Affinity,
  caster: { kind: "player" | "enemy" },
  candidate: { kind: "player" | "enemy" },
): boolean {
  return affinity === "hostile" ? candidate.kind !== caster.kind : candidate.kind === caster.kind;
}
