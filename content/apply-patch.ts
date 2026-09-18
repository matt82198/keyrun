/**
 * content/apply-patch.ts
 * The deep-merge patch engine shared by talent options and gear facets in content/apply.ts. Split
 * out of apply.ts so every apply-*.ts file stays under the sim/CLAUDE.md 190 LOC cap -- see
 * content/apply.ts's header for the full deep-merge semantics contract this implements.
 */
import type { AbilityCost, AbilityDef, EffectOp, ShapeDef } from "./schema/ability";
import type { FacetPatch } from "./schema/facet";

const STRUCTURED_MERGE_KEYS = new Set(["tagAdd", "tagRemove", "effectsAppend"]);
const DIRECT_PATCH_KEYS = new Set([
  "name", "castTicks", "gcdTicks", "cooldownTicks", "charges", "cost",
  "range", "shape", "interruptible", "tags", "effects", "variants",
]);

/**
 * Applies one patch (a talent option's or a facet's) to a base AbilityDef and returns a new,
 * deep-merged AbilityDef. Never mutates `base`. Throws on an `id`-changing patch or an
 * unrecognised patch field.
 */
export function applyPatch(base: AbilityDef, patch: FacetPatch, patchLabel: string): AbilityDef {
  const raw = patch as Record<string, unknown>;
  if (Object.prototype.hasOwnProperty.call(raw, "id") && raw.id !== base.id) {
    throw new Error(
      `applyLoadout: ${patchLabel} may not change ability id (base "${base.id}" -> patch "${String(raw.id)}")`,
    );
  }

  const next: AbilityDef = structuredClone(base);
  for (const key of Object.keys(raw)) {
    if (key === "id") continue; // already verified equal to base.id above
    if (STRUCTURED_MERGE_KEYS.has(key)) {
      switch (key) {
        case "tagAdd": {
          const toAdd = raw.tagAdd as string[];
          next.tags = Array.from(new Set([...next.tags, ...toAdd]));
          break;
        }
        case "tagRemove": {
          const toRemove = new Set(raw.tagRemove as string[]);
          next.tags = next.tags.filter((t) => !toRemove.has(t));
          break;
        }
        case "effectsAppend": {
          next.effects = [...next.effects, ...(raw.effectsAppend as EffectOp[])];
          break;
        }
      }
      continue;
    }
    if (!DIRECT_PATCH_KEYS.has(key)) {
      throw new Error(`applyLoadout: ${patchLabel} has unrecognised patch field "${key}"`);
    }
    switch (key) {
      case "name": next.name = raw.name as string; break;
      case "castTicks": next.castTicks = raw.castTicks as number; break;
      case "gcdTicks": next.gcdTicks = raw.gcdTicks as number; break;
      case "cooldownTicks": next.cooldownTicks = raw.cooldownTicks as number; break;
      case "charges": next.charges = raw.charges as number; break;
      case "cost": next.cost = raw.cost as AbilityCost; break;
      case "range": next.range = raw.range as number; break;
      case "shape": next.shape = raw.shape as ShapeDef; break;
      case "interruptible": next.interruptible = raw.interruptible as boolean; break;
      case "tags": next.tags = raw.tags as string[]; break;
      case "effects": next.effects = raw.effects as EffectOp[]; break;
      case "variants": next.variants = raw.variants as Record<string, EffectOp[]>; break;
    }
  }
  return next;
}
