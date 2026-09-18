/**
 * content/schema/ability.ts
 * The real AbilityDef/EffectOp/AbilityShape contract lives in sim/combat/abilities.ts (landed via
 * PR #10/#13); this module no longer carries a local duplicate of it. `EffectOp`/`AbilityShape`
 * are plain re-exports. `SimAbilityDef` is the bare real type, for anyone who wants to say so
 * explicitly.
 *
 * `AbilityDef` -- the name every other content/** module (content/apply.ts, content/index.ts,
 * content/schema/class.ts, content/schema/facet.ts) already imports from here -- is the real sim
 * type PLUS the two fields it has no notion of and that this codebase already depends on under
 * this exact name: a display `name` (sim never renders anything) and the Chanter stance-variant
 * effect override `variants` (see content/CLAUDE.md "Schema additions"). `tags` is promoted from
 * optional (the real sim default, for fixtures/tests that predate Windows and Combos) to required,
 * because every content/data/abilities/*.json entry always sets one (even if empty) and
 * content/apply.ts reads `.tags` unconditionally. A value of this richer `AbilityDef` is always a
 * structurally valid `SimAbilityDef` (RulesConfig.abilities / RunContent.abilities accept
 * content's registry, or any single ability out of it, as-is) -- the extra fields and the
 * `tags?` -> `tags` tightening only ever narrow what's *allowed in*, never what a consumer that
 * only reads the real sim fields can rely on.
 *
 * `ShapeDef`/`AbilityCost` are back-compat aliases for the pre-integration names of
 * `AbilityDef["shape"]`/`AbilityDef["cost"]`, kept as plain indexed-access aliases of the real
 * type (never a second, driftable declaration) because content/apply.ts and
 * content/schema/facet.ts still import them under these names.
 */
export type { EffectOp, AbilityShape } from "../../sim/combat/abilities";
export type { AbilityDef as SimAbilityDef } from "../../sim/combat/abilities";
export { validateAbilityDef, abilitySchool } from "../../sim/combat/abilities";

import type { AbilityDef as SimAbilityDef, EffectOp } from "../../sim/combat/abilities";

export type ShapeDef = SimAbilityDef["shape"];
export type AbilityCost = SimAbilityDef["cost"];

export interface AbilityDef extends Omit<SimAbilityDef, "tags"> {
  name: string;
  tags: string[];
  /**
   * Per-stance effect override, used only by Chanter (`"mend"` / `"ward"` keys) so one ability
   * plays two ways without a second ability id. Absent = `effects` always applies. Not part of
   * the sim contract: sim/combat/casting.ts's completeCast always uses `def.effects` -- a future
   * stance-selection layer above sim (or content/apply.ts) is expected to pre-resolve the active
   * variant into a plain AbilityDef.effects before handing it to sim, exactly like a talent patch.
   */
  variants?: Record<string, EffectOp[]>;
}

/** @deprecated alias for `AbilityDef` from an earlier draft of this lane's rewrite; prefer
 * `AbilityDef` directly (this is the type content/index.ts's registries actually hold). */
export type ContentAbilityDef = AbilityDef;
