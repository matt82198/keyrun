/**
 * Wildcard removeEffect (`op: "removeEffect", effectId: "*"`, content/CLAUDE.md's "dispel
 * convention"): Cinder's Purge and Chanter's Cleanse both express dispel this way, and the
 * `EffectOp` schema (sim/combat/abilities.ts, out of this domain's edit scope) has no field
 * saying whether a given dispel is meant to strip a buff or a debuff -- the design intent
 * (design/BIBLE.md: Cleanse removes hostile effects from an ally, Purge removes beneficial
 * effects from an enemy) is derived instead from fields that already exist. `removeDispellable
 * Effects` below does that: the caster's `kind` relative to the target's stands in for ally/enemy
 * (same `kind` = ally -- every cast in the bible's kit list targets the caster's own side or the
 * enemy, never PvP), and `effectPolarity` derives buff/debuff from each dispellable effect's own
 * modifiers, since the schema carries no explicit polarity flag either. Only effects with a
 * non-null `dispellableTag` are eligible, matching the dispel-immune convention every
 * non-dispellable effect in content already relies on. Effects with no modifier signal at all
 * (e.g. stunned/silenced/blinded, which gate other systems rather than carry a modifier) default
 * to hostile: every such effect in current content is a debuff, and failing closed toward
 * "hostile" is what lets Cleanse strip crowd control. Removal iterates `actor.effects` in place
 * (insertion order), so which instance goes first is always the order it was applied in --
 * deterministic without a separate instance-id field.
 * Usage: removeDispellableEffects(sourceActor, targetActor, effectDefs);
 */
import type { ActorState } from "./types";
import type { EffectDef } from "./effects";

export type EffectPolarity = "beneficial" | "hostile";

/**
 * Classifies a dispellable effect as a buff or a debuff from its own modifiers (see file
 * docstring): a modifier that helps the holder is beneficial, one that hurts them is hostile.
 * Effects with no modifier signal (pure gate-flags like stunned/silenced/blinded) default to
 * hostile, matching every such effect in current content.
 */
export function effectPolarity(def: EffectDef): EffectPolarity {
  const m = def.modifiers;
  const beneficial =
    (m.damageTakenBp ?? 0) < 0 ||
    (m.movementBp ?? 0) > 0 ||
    (m.hasteBp ?? 0) > 0 ||
    (m.damageDoneBp ?? 0) > 0 ||
    (m.absorb ?? 0) > 0 ||
    def.periodic?.op.op === "heal";
  const hostile =
    (m.damageTakenBp ?? 0) > 0 ||
    (m.movementBp ?? 0) < 0 ||
    (m.hasteBp ?? 0) < 0 ||
    (m.damageDoneBp ?? 0) < 0 ||
    def.periodic?.op.op === "damage";
  return beneficial && !hostile ? "beneficial" : "hostile";
}

/**
 * Implements the removeEffect op's `effectId: "*"` wildcard: strips `target`'s dispellable
 * effects whose polarity opposes `source`'s relation to it (see file docstring for the full
 * rule). Same `kind` (ally) removes hostile effects (Cleanse); different `kind` (enemy) removes
 * beneficial effects (Purge). No-op if the target carries nothing eligible.
 */
export function removeDispellableEffects(
  source: Pick<ActorState, "kind">,
  target: ActorState,
  defs: Record<string, EffectDef>,
): void {
  const isAlly = source.kind === target.kind;
  const removePolarity: EffectPolarity = isAlly ? "hostile" : "beneficial";
  target.effects = target.effects.filter((inst) => {
    const def = defs[inst.effectId];
    if (!def || def.dispellableTag === null) return true;
    return effectPolarity(def) !== removePolarity;
  });
}
