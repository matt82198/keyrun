/**
 * content/schema/ability.ts
 * Re-exports the canonical AbilityDef/EffectOp/AbilityShape contract from sim/combat/abilities.ts
 * (landed on main via PR #10/#13) instead of carrying a local duplicate. `ContentAbilityDef`
 * layers the two fields the real sim type has no notion of -- a display `name` (sim never renders
 * anything) and the Chanter stance-variant effect override (see content/CLAUDE.md "Schema
 * additions") -- onto the real `AbilityDef`. Every content/data/abilities/*.json entry is a
 * ContentAbilityDef; `content/index.ts`'s `abilities` registry holds these, but any single value
 * plucked out of it is a structurally valid sim AbilityDef (RulesConfig.abilities /
 * RunContent.abilities accept it as-is).
 */
export type { AbilityDef, EffectOp, AbilityShape } from "../../sim/combat/abilities";
export { validateAbilityDef, abilitySchool } from "../../sim/combat/abilities";

import type { AbilityDef, EffectOp } from "../../sim/combat/abilities";

export interface ContentAbilityDef extends AbilityDef {
  name: string;
  /**
   * Per-stance effect override, used only by Chanter (`"mend"` / `"ward"` keys) so one ability
   * plays two ways without a second ability id. Absent = `effects` always applies. Not part of
   * the sim contract: sim/combat/casting.ts's completeCast always uses `def.effects` -- a future
   * stance-selection layer above sim (or content/apply.ts) is expected to pre-resolve the active
   * variant into a plain AbilityDef.effects before handing it to sim, exactly like a talent patch.
   */
  variants?: Record<string, EffectOp[]>;
}
