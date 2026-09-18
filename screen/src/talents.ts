/**
 * Usage: import { setTalentChoice, computeLoadout, type LoadoutSummary } from "./talents.js";
 *
 * Free respec (design/BIBLE.md sec 6): `setTalentChoice` sets or clears one forked ability's
 * talent pick, validated against the class's real talent forks (content/data/classes/*.json). No
 * cost, no cooldown -- picking is instant and reversible any time on the screen. `computeLoadout`
 * wraps content/apply.ts's `applyLoadout` (which itself runs `describeLoadout`) for the Character
 * panel's live build summary.
 */
import { applyLoadout, type LoadoutResult } from "../../content/apply.js";
import { getAbilityRegistry, getClassDef, getFacetRegistry } from "./content.js";
import { GEAR_SLOT_IDS, type Character } from "./types.js";

/** Sets (or, with `optionId: null`, clears back to the base ability) one forked ability's talent
 * pick. No-op for an ability with no fork, or an optionId that fork doesn't offer. */
export function setTalentChoice(character: Character, abilityId: string, optionId: string | null): Character {
  const classDef = getClassDef(character.classId);
  const fork = classDef.talents.find((f) => f.abilityId === abilityId);
  if (!fork) return character;
  if (optionId === null) {
    const { [abilityId]: _removed, ...rest } = character.talentChoices;
    return { ...character, talentChoices: rest };
  }
  if (!fork.options.some((o) => o.id === optionId)) return character;
  return { ...character, talentChoices: { ...character.talentChoices, [abilityId]: optionId } };
}

export interface LoadoutSummary {
  result: LoadoutResult | null;
  error: string | null;
}

/** Applies the character's current talents + gear to their class's abilities (content/apply.ts),
 * for the Character panel's live "what changed" summary. Never throws -- a bad state (e.g. an
 * invariant violation) is reported as `error` instead of crashing the panel. */
export function computeLoadout(character: Character): LoadoutSummary {
  try {
    const classDef = getClassDef(character.classId);
    const gear = GEAR_SLOT_IDS.map((slot) => character.gear[slot]);
    const result = applyLoadout(classDef, getAbilityRegistry(), getFacetRegistry(), character.talentChoices, gear);
    return { result, error: null };
  } catch (err) {
    return { result: null, error: err instanceof Error ? err.message : String(err) };
  }
}
