/**
 * content/apply-describe.ts
 * Build-summary rendering for a resolved loadout: which abilities ended up different from their
 * base definition, and which fields changed. Independent of the merge itself so it can be re-run
 * later against a stored/serialized result without redoing applyLoadout's work. Split out of
 * content/apply.ts so every apply-*.ts file stays under the sim/CLAUDE.md 190 LOC cap.
 */
import type { AbilityDef } from "./schema/ability";
import type { ClassDef } from "./schema/class";
import { ALL_SLOTS, slotAbilityMap } from "./apply-types";

/**
 * Renders one line per ability whose final definition differs from its base definition -- for
 * the character screen's build summary.
 */
export function describeLoadout(
  classDef: ClassDef,
  base: ReadonlyMap<string, AbilityDef>,
  abilities: Record<string, AbilityDef>,
): string[] {
  const slots = slotAbilityMap(classDef);
  const lines: string[] = [];
  for (const slot of ALL_SLOTS) {
    const id = slots[slot];
    if (!id) continue;
    const before = base.get(id);
    const after = abilities[id];
    if (!before || !after) continue;
    if (JSON.stringify(before) === JSON.stringify(after)) continue;
    lines.push(`${after.name} (${id}): ${summarizeChange(before, after)}`);
  }
  return lines;
}

const DESCRIBABLE_FIELDS = [
  "tags", "effects", "shape", "cooldownTicks", "cost", "castTicks",
  "gcdTicks", "charges", "range", "interruptible", "variants",
] as const satisfies readonly (keyof AbilityDef)[];

function summarizeChange(before: AbilityDef, after: AbilityDef): string {
  const changed: string[] = [];
  for (const field of DESCRIBABLE_FIELDS) {
    if (JSON.stringify(before[field]) !== JSON.stringify(after[field])) changed.push(field);
  }
  return changed.length > 0 ? `changed ${changed.join(", ")}` : "changed";
}
