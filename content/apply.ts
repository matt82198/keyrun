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
 * field -- see content/schema/facet.ts) -- implemented in content/apply-patch.ts:
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
 *
 * This file holds only the top-level orchestration (`applyLoadout`); the patch-merge engine
 * lives in content/apply-patch.ts, the shared slot/type helpers in content/apply-types.ts, and
 * the build-summary renderer in content/apply-describe.ts (re-exported here so importers keep
 * using a single `@content/apply` entry point).
 */

import type { AbilityDef } from "./schema/ability";
import type { ClassDef, TalentOption } from "./schema/class";
import type { FacetDef, GearSlotId, Item } from "./schema/facet";
import { GEAR_SLOT_IDS } from "./schema/facet";
import { applyPatch } from "./apply-patch";
import { describeLoadout } from "./apply-describe";
import { ALL_SLOTS, ROTATIONAL_SLOTS, isPayoffTag, isSetupTag, slotAbilityMap } from "./apply-types";
import type { LoadoutResult, TalentChoices } from "./apply-types";

export type { TalentChoices, LoadoutResult } from "./apply-types";
export { describeLoadout } from "./apply-describe";

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
