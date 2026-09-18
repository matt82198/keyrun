/**
 * content/apply.ts
 * BIBLE.md Sec 6 "Talents" and Sec 7 "Gear and progression": talents are forks that patch what
 * an ability does; gear Facets retag or reshape an ability. Both must be applied to a class's
 * ability definitions at run start, deterministically, before the sim ever sees them.
 *
 * Usage:
 *   import { applyLoadout, describeLoadout } from "@content/apply";
 *   const { abilities, log } = applyLoadout(classDef, abilityRegistry, facetRegistry, talentChoices, gear);
 *
 * `applyLoadout` is a pure function: same (classDef, abilities, facets, talentChoices, gear) in
 * always produces a deep-equal result out. It never mutates its inputs (base abilities are deep
 * cloned before any patch is applied) and never touches the filesystem, sim/, or a PRNG.
 *
 * Order: base ability -> talent patch (at most one option per forked ability, from
 * `classDef.talents`) -> facet patch (at most one per gear slot, from the slot's facet pool).
 * Facets are applied in canonical slot order (weapon, armor, charm, ring, keystone), not gear
 * array order, so two facets that happen to target the same ability id still merge
 * deterministically regardless of how the caller ordered the `gear` array.
 *
 * Deep-merge semantics for a patch (`Partial<AbilityDef>` field, or a `FacetStructuredPatch`
 * field -- see content/schema/facet.ts):
 *   - Every field is a full replacement of the base ability's field EXCEPT:
 *     - `tagAdd`   -- merged into `tags` (added if not already present).
 *     - `tagRemove`-- removed from `tags`.
 *     - `effectsAppend` -- appended onto `effects` (base effects kept, new ones added after).
 *   - A patch may never set `id` to a value different from the ability it targets -- that would
 *     smuggle in a 6th rotational ability or silently rename one. Refused with a thrown Error.
 *   - Any patch field name that is not a recognised AbilityDef field or one of the three merge
 *     keys above throws -- content typos fail loudly rather than being silently dropped.
 *
 * Deviation from the dispatch-specified signature `applyLoadout(classDef, talentChoices, gear)`:
 * ClassDef/TalentFork/FacetDef only store ability/facet *ids* (strings) -- there is no way to
 * patch an AbilityDef without also being handed the actual ability and facet registries. Rather
 * than doing file I/O inside this "pure" function (which content/CLAUDE.md forbids -- content
 * has no sim/ logic and this module should not reach into content/index.ts's readJson), the
 * registries are explicit parameters. This keeps applyLoadout a pure function of its arguments
 * and keeps it trivially unit-testable against small fixtures instead of the full, 3-class
 * dataset. See this lane's final report for the full rationale.
 */

import type { AbilityCost, AbilityDef, EffectOp, ShapeDef } from "./schema/ability";
import type { ClassDef, TalentOption } from "./schema/class";
import type { FacetDef, FacetPatch, GearSlotId, Item } from "./schema/facet";
import { GEAR_SLOT_IDS } from "./schema/facet";

const ROTATIONAL_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;
const ALL_SLOTS = [...ROTATIONAL_SLOTS, "interrupt", "defensive", "utility", "movement"] as const;

/** `talentChoices` maps a forked ability's id to the chosen `TalentOption.id`. One per ability. */
export type TalentChoices = Record<string, string>;

export interface LoadoutResult {
  /** Every ability id the class uses (rotational + interrupt/defensive/utility/movement). */
  abilities: Record<string, AbilityDef>;
  /** One line per ability that ended up different from its base definition. */
  log: string[];
}

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
function applyPatch(base: AbilityDef, patch: FacetPatch, patchLabel: string): AbilityDef {
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

function slotAbilityMap(classDef: ClassDef): Record<string, string> {
  return classDef.abilities as unknown as Record<string, string>;
}

/**
 * Renders one line per ability whose final definition differs from its base definition -- for
 * the character screen's build summary. Independent of `applyLoadout` so it can be re-run later
 * against a stored/serialized result without redoing the merge.
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

function isSetupTag(tag: string): boolean {
  return tag.startsWith("setup:");
}

function isPayoffTag(tag: string): boolean {
  return tag === "payoff";
}

/**
 * Applies talent forks then gear facets to a class's ability definitions, in that order, and
 * validates the BIBLE.md invariants: no patch changes an ability's id (so no patch can smuggle
 * in a 6th rotational ability), and the resulting 5 rotational abilities carry exactly 2 Setup
 * and exactly 2 Payoff tags. Throws with a clear, listable error on any violation. Pure and
 * deterministic: never mutates its inputs, never reads the filesystem.
 */
export function applyLoadout(
  classDef: ClassDef,
  abilities: ReadonlyMap<string, AbilityDef>,
  facets: ReadonlyMap<string, FacetDef>,
  talentChoices: TalentChoices,
  gear: readonly Item[],
): LoadoutResult {
  const slots = slotAbilityMap(classDef);
  const classAbilityIds = ALL_SLOTS.map((slot) => {
    const id = slots[slot];
    if (!id) throw new Error(`applyLoadout: class "${classDef.id}" is missing ability slot "${slot}"`);
    return id;
  });

  const base = new Map<string, AbilityDef>();
  for (const id of classAbilityIds) {
    const def = abilities.get(id);
    if (!def) {
      throw new Error(`applyLoadout: class "${classDef.id}" ability "${id}" is missing from the ability registry`);
    }
    base.set(id, def);
  }

  const working = new Map<string, AbilityDef>();
  for (const [id, def] of base) working.set(id, structuredClone(def));

  // --- talents: one option per forked ability, validated ---
  const forkByAbility = new Map(classDef.talents.map((fork) => [fork.abilityId, fork]));
  for (const abilityId of Object.keys(talentChoices)) {
    if (!forkByAbility.has(abilityId)) {
      throw new Error(`applyLoadout: class "${classDef.id}" has no talent fork for ability "${abilityId}"`);
    }
  }
  for (const fork of classDef.talents) {
    const optionId = talentChoices[fork.abilityId];
    if (optionId === undefined) continue; // no choice made: ability stays at its base definition
    const option: TalentOption | undefined = fork.options.find((o) => o.id === optionId);
    if (!option) {
      throw new Error(
        `applyLoadout: class "${classDef.id}" ability "${fork.abilityId}" has no talent option "${optionId}"`,
      );
    }
    const current = working.get(fork.abilityId);
    if (!current) throw new Error(`applyLoadout: talent fork ability "${fork.abilityId}" not in working set`);
    working.set(
      fork.abilityId,
      applyPatch(current, option.patch, `talent "${option.id}" on "${fork.abilityId}"`),
    );
  }

  // --- facets: at most one item per slot, validated against the slot's pool and the class ---
  const seenSlots = new Set<GearSlotId>();
  for (const item of gear) {
    if (seenSlots.has(item.slot)) {
      throw new Error(`applyLoadout: more than one item equipped in slot "${item.slot}"`);
    }
    seenSlots.add(item.slot);
  }

  const classAbilityIdSet = new Set(classAbilityIds);
  const gearInSlotOrder = [...gear].sort(
    (a, b) => GEAR_SLOT_IDS.indexOf(a.slot) - GEAR_SLOT_IDS.indexOf(b.slot),
  );
  for (const item of gearInSlotOrder) {
    if (item.facet === null) continue;
    const facetDef = facets.get(item.facet.id);
    if (!facetDef) {
      throw new Error(`applyLoadout: unknown facet id "${item.facet.id}" in slot "${item.slot}"`);
    }
    if (facetDef.slot !== item.slot) {
      throw new Error(
        `applyLoadout: facet "${facetDef.id}" belongs to slot "${facetDef.slot}", not "${item.slot}"`,
      );
    }
    if (facetDef.abilityId !== item.facet.abilityId) {
      throw new Error(
        `applyLoadout: facet "${facetDef.id}" targets ability "${facetDef.abilityId}", item claims "${item.facet.abilityId}"`,
      );
    }
    if (!classAbilityIdSet.has(facetDef.abilityId)) {
      throw new Error(
        `applyLoadout: facet "${facetDef.id}" targets ability "${facetDef.abilityId}", which class "${classDef.id}" does not use`,
      );
    }
    const current = working.get(facetDef.abilityId);
    if (!current) throw new Error(`applyLoadout: facet ability "${facetDef.abilityId}" not in working set`);
    working.set(facetDef.abilityId, applyPatch(current, facetDef.patch, `facet "${facetDef.id}"`));
  }

  // --- invariant: every rotational ability kept its identity (belt-and-braces; applyPatch
  // already refuses id-changing patches, so this can only fire on an internal bug) ---
  for (const slot of ROTATIONAL_SLOTS) {
    const id = slots[slot];
    const ability = id ? working.get(id) : undefined;
    if (!id || !ability || ability.id !== id) {
      throw new Error(`applyLoadout: rotational slot "${slot}" lost its ability identity`);
    }
  }

  // --- invariant: exactly 2 Setup + 2 Payoff rotational abilities (BIBLE.md Sec 5/6/7) ---
  const rotAbilities = ROTATIONAL_SLOTS.map((slot) => working.get(slots[slot] as string) as AbilityDef);
  const setups = rotAbilities.filter((a) => a.tags.some(isSetupTag));
  const payoffs = rotAbilities.filter((a) => a.tags.some(isPayoffTag));
  if (setups.length !== 2 || payoffs.length !== 2) {
    const detail = rotAbilities.map((a) => `${a.id}=[${a.tags.join(",")}]`).join(", ");
    throw new Error(
      `applyLoadout: class "${classDef.id}" loadout would leave ${setups.length} setup / ${payoffs.length} payoff ` +
        `rotational abilities (bible invariant requires exactly 2/2). Rotational tags: ${detail}`,
    );
  }

  const resultAbilities: Record<string, AbilityDef> = {};
  for (const id of classAbilityIds) {
    const ability = working.get(id);
    if (!ability) throw new Error(`applyLoadout: ability "${id}" missing from working set at finalize`);
    resultAbilities[id] = ability;
  }

  return { abilities: resultAbilities, log: describeLoadout(classDef, base, resultAbilities) };
}
