/**
 * bots/src/kit-heals.ts
 *
 * Shape-aware heal and taunt classification for kits.ts's buildClassKit(). A heal is only useful
 * to a healer bot if its SHAPE can reach the ally that needs it: sim/combat/targeting.ts's
 * resolveTargets applies a "self" ability to the caster only, a "target" ability to the caster's
 * explicit target, and a "circle" ability to every friendly within the radius AROUND THE CASTER.
 * The single `healAbilityId` (first rotational ability with a heal op) picked Chanter's Refrain --
 * a self-shaped heal -- so the healer bot "healed" a dying tank by healing itself (pull-0 repro,
 * tests/sim/integration/bots-pull0.test.ts). This file splits heals by reach:
 *   - selfHeal:   self-shaped heal (Refrain/Mend) -- only for the healer's own health;
 *   - groupHeal:  circle-shaped heal around the caster (Chorus/Mend) + its radius;
 *   - targetHeal: target-shaped ability that heals directly, or applies an effect whose periodic
 *                 op heals (Cadence/Mend's mendingHot HoT) -- the one heal that reaches anyone.
 * Variants: like kit-resources.ts's healsOnCast, the "mend" variant counts (client/server
 * resolve every variant ability to its mend variant, see client/src/sim-host/run-setup.ts).
 *
 * Taunt: the class's utility ability when it carries a `{op:"threat"}` op. sim generates threat
 * ONLY from explicit threat ops (sim/combat/casting.ts's applyEffects; damage adds none), so a
 * tank that never casts one never holds an enemy at all.
 *
 * Usage: `const heals = classifyHeals(rotationalDefs, effects); const taunt = tauntAbilityId(def);`
 */
import type { AbilityDef } from "@content/schema/ability";
import type { EffectDef } from "@content/schema/effect";

type Op = AbilityDef["effects"][number];

export interface HealKit {
  selfHealAbilityId: string | null;
  groupHealAbilityId: string | null;
  groupHealRadiusMm: number;
  targetHealAbilityId: string | null;
}

function castOps(def: AbilityDef): Op[] {
  return def.variants?.mend ?? def.effects;
}

function opHeals(op: Op, effects: ReadonlyMap<string, EffectDef> | undefined): boolean {
  if (op.op === "heal") return true;
  if (op.op === "applyEffect" && effects) return effects.get(op.effectId)?.periodic?.op.op === "heal";
  return false;
}

function heals(def: AbilityDef, effects: ReadonlyMap<string, EffectDef> | undefined): boolean {
  return castOps(def).some((op) => opHeals(op, effects));
}

export function classifyHeals(defs: readonly AbilityDef[], effects?: ReadonlyMap<string, EffectDef>): HealKit {
  const kit: HealKit = { selfHealAbilityId: null, groupHealAbilityId: null, groupHealRadiusMm: 0, targetHealAbilityId: null };
  for (const def of defs) {
    if (!heals(def, effects)) continue;
    if (def.shape.kind === "self" && kit.selfHealAbilityId === null) kit.selfHealAbilityId = def.id;
    else if (def.shape.kind === "circle" && kit.groupHealAbilityId === null) {
      kit.groupHealAbilityId = def.id;
      kit.groupHealRadiusMm = def.shape.radius ?? 0;
    } else if (def.shape.kind === "target" && kit.targetHealAbilityId === null) kit.targetHealAbilityId = def.id;
  }
  return kit;
}

export function tauntAbilityId(utility: AbilityDef | undefined): string | null {
  if (!utility || utility.shape.kind !== "target") return null;
  return utility.effects.some((op) => op.op === "threat") ? utility.id : null;
}
