/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers clauses.ts: the pick-of-three Clause chooser's determinism (design/BIBLE.md sec 8 --
 * "before queueing, the party is offered three modifiers and picks one; exactly one Clause runs
 * at a time, never stacked" -- "none" is never offered), ensureClauseSelected's invariant
 * (a valid pick always exists), selectClause, and the Play URL carrying the chosen Clause id.
 */
import { describe, expect, it } from "vitest";
import {
  buildPlayUrl,
  createFixtureCharacter,
  ensureClauseSelected,
  pickClauseOffers,
  selectClause,
} from "../../screen/src/store.js";

describe("pickClauseOffers", () => {
  it("always offers exactly three distinct Clauses, never 'none'", () => {
    const offers = pickClauseOffers(5);
    expect(offers).toHaveLength(3);
    const ids = offers.map((c) => c.id);
    expect(new Set(ids).size).toBe(3);
    expect(ids).not.toContain("none");
  });

  it("same key level -> same three offers", () => {
    const a = pickClauseOffers(8).map((c) => c.id);
    const b = pickClauseOffers(8).map((c) => c.id);
    expect(a).toEqual(b);
  });

  it("a different key level draws a different set of offers", () => {
    const low = pickClauseOffers(2).map((c) => c.id);
    const high = pickClauseOffers(19).map((c) => c.id);
    expect(low).not.toEqual(high);
  });
});

describe("ensureClauseSelected", () => {
  it("is a no-op when the current pick is still one of the key level's offers", () => {
    const character = createFixtureCharacter(1000);
    const offers = pickClauseOffers(character.keyLevel);
    expect(offers.some((c) => c.id === character.clauseId)).toBe(true);
    expect(ensureClauseSelected(character)).toBe(character);
  });

  it("resets to the first offer when the stored pick has drifted off the current offers", () => {
    const character = { ...createFixtureCharacter(1000), clauseId: "not-a-real-clause-id" };
    const fixed = ensureClauseSelected(character);
    const offers = pickClauseOffers(character.keyLevel);
    expect(fixed.clauseId).toBe(offers[0]!.id);
  });
});

describe("selectClause", () => {
  it("sets the pick when it is one of the current offers", () => {
    const character = createFixtureCharacter(1000);
    const offers = pickClauseOffers(character.keyLevel);
    const other = offers.find((c) => c.id !== character.clauseId) ?? offers[0]!;
    const next = selectClause(character, other.id);
    expect(next.clauseId).toBe(other.id);
  });

  it("is a no-op for an id that is not currently offered", () => {
    const character = createFixtureCharacter(1000);
    const next = selectClause(character, "not-currently-offered");
    expect(next).toBe(character);
  });
});

describe("buildPlayUrl", () => {
  it("contains a numeric seed, the clamped key level, and the Clause id", () => {
    const character = createFixtureCharacter(1000);
    const url = buildPlayUrl(character, 12, "hush");
    expect(url).toMatch(/^\.\.\/client\/\?seed=\d+&key=12&clause=hush$/);
  });

  it("clamps an out-of-range key level before building the url", () => {
    const character = createFixtureCharacter(1000);
    const url = buildPlayUrl(character, 99, "echo");
    expect(url).toContain("key=20");
    expect(url).toContain("clause=echo");
  });
});
