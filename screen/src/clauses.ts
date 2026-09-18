/**
 * Usage: import { pickClauseOffers, ensureClauseSelected, selectClause, deriveSeed, buildPlayUrl }
 *          from "./clauses.js";
 *
 * The Run band's pick-of-three Clause chooser (design/BIBLE.md sec 8: "before queueing, the
 * party is offered three modifiers and picks one; exactly one Clause runs at a time, never
 * stacked" -- "none" is never offered). The three offers are a pure, deterministic function of
 * the key level and the dungeon id, via sim/core/prng.ts's xoshiro128** (not the ad hoc
 * hashString by itself, which only seeds it) -- same key -> same three offers, a different key
 * -> a different draw. The chosen Clause id rides in the Play URL as `?clause=<id>`.
 */
import { nextInt, seedState } from "../../sim/core/prng.js";
import { DUNGEON_ID, getClauses } from "./content.js";
import { hashString } from "./hash.js";
import { clampKeyLevel } from "./rules.js";
import type { ClauseDef } from "../../content/schema/clause.js";
import type { Character } from "./types.js";

function deriveClauseSeed(keyLevel: number): number {
  return hashString(`${DUNGEON_ID}:${clampKeyLevel(keyLevel)}`);
}

/** Three distinct Clause offers, seeded from the key level (and the POC's one dungeon id). */
export function pickClauseOffers(keyLevel: number): [ClauseDef, ClauseDef, ClauseDef] {
  const clauses = getClauses();
  let state = seedState(deriveClauseSeed(keyLevel));
  const indices: number[] = [];
  while (indices.length < Math.min(3, clauses.length)) {
    const [idx, next] = nextInt(state, clauses.length);
    state = next;
    if (!indices.includes(idx)) indices.push(idx);
  }
  return [clauses[indices[0]!]!, clauses[indices[1]!]!, clauses[indices[2]!]!];
}

/** Guarantees `character.clauseId` is one of the current key level's three offers, defaulting to
 * the first offer when it drifted (e.g. the key level changed since the last pick). No-op (same
 * reference) when the existing pick is still one of the offers. */
export function ensureClauseSelected(character: Character): Character {
  const offers = pickClauseOffers(character.keyLevel);
  if (offers.some((c) => c.id === character.clauseId)) return character;
  return { ...character, clauseId: offers[0].id };
}

/** Player picks one of the three current offers. No-op for an id that is not currently offered. */
export function selectClause(character: Character, clauseId: string): Character {
  const offers = pickClauseOffers(character.keyLevel);
  if (!offers.some((c) => c.id === clauseId)) return character;
  return { ...character, clauseId };
}

export function deriveSeed(character: Pick<Character, "classId" | "level" | "runHistory">, keyLevel: number): number {
  const basis = `${character.classId}:${character.level}:${keyLevel}:${character.runHistory.length}`;
  return hashString(basis);
}

/** The Play button's target: the local client, given the run's seed, key level, and Clause pick. */
export function buildPlayUrl(
  character: Pick<Character, "classId" | "level" | "runHistory">,
  keyLevel: number,
  clauseId: string,
): string {
  const key = clampKeyLevel(keyLevel);
  const seed = deriveSeed(character, key);
  return `../client/?seed=${seed}&key=${key}&clause=${encodeURIComponent(clauseId)}`;
}
