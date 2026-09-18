/**
 * content/schema/effect.ts
 * Re-exports the real EffectDef/EffectModifiers/EffectPeriodic contract from
 * sim/combat/effects.ts. `ContentEffectDef` adds only a display `name` (sim never renders
 * anything); every other content-lane addition from the pre-integration draft has been dropped
 * because the real type already covers it or has no room for it:
 *   - `durationTicks` doesn't exist on the real EffectDef -- duration is supplied at apply time by
 *     whichever AbilityDef/ComboDef `applyEffect` op references this effect id (each already
 *     carries its own `durationTicks`), never stored on the def itself.
 *   - `dispellable` is renamed to the real field `dispellableTag` (still just a `string | null`
 *     tag, "magic"/"physical" remain valid tag values -- no type change, just the field name).
 *   - `modifiers.controlKind` (stun/silence/root/blind) has NO real-sim equivalent: the shipped
 *     EffectModifiers is basis-points-only (damageDoneBp/damageTakenBp/hasteBp/movementBp/absorb)
 *     and sim/combat/casting.ts's startCast never consults an actor's effects for a control state
 *     (only a school lockout). Hard control is currently a naming convention only -- the
 *     `stunned`/`silenced`/`blinded` effect ids in content/data/effects/player.json apply and
 *     expire like any other EffectInstance but do not mechanically prevent a cast or move. Flagged
 *     as a sim gap in this lane's final report; not fixed here (sim/** is out of this lane's scope).
 */
export type { EffectDef, EffectModifiers } from "../../sim/combat/effects";

import type { EffectDef } from "../../sim/combat/effects";

/** The real EffectDef inlines this shape on `.periodic` rather than naming it; named here purely
 * for convenience when authoring content data. */
export type EffectPeriodic = NonNullable<EffectDef["periodic"]>;

export interface ContentEffectDef extends EffectDef {
  name: string;
}
