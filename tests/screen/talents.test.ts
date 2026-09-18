/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers talents.ts: free respec (design/BIBLE.md sec 6) via setTalentChoice, and computeLoadout's
 * live build summary (content/apply.ts's describeLoadout) reacting to a talent change.
 */
import { describe, expect, it } from "vitest";
import { computeLoadout, createFixtureCharacter, setTalentChoice } from "../../screen/src/store.js";

describe("setTalentChoice", () => {
  it("sets a forked ability's talent pick", () => {
    const character = createFixtureCharacter(1000);
    const next = setTalentChoice(character, "warden.strike", "warcry");
    expect(next.talentChoices["warden.strike"]).toBe("warcry");
  });

  it("free respec: changing the pick again just overwrites it, no cost or cooldown fields exist to pay", () => {
    let character = createFixtureCharacter(1000);
    character = setTalentChoice(character, "warden.strike", "warcry");
    character = setTalentChoice(character, "warden.strike", "riposte");
    expect(character.talentChoices["warden.strike"]).toBe("riposte");
  });

  it("clears back to the base ability when passed null", () => {
    let character = createFixtureCharacter(1000);
    character = setTalentChoice(character, "warden.strike", "warcry");
    character = setTalentChoice(character, "warden.strike", null);
    expect(character.talentChoices["warden.strike"]).toBeUndefined();
  });

  it("is a no-op for an ability with no talent fork", () => {
    const character = createFixtureCharacter(1000);
    const next = setTalentChoice(character, "warden.taunt", "anything");
    expect(next).toBe(character);
  });

  it("is a no-op for an option id the fork does not offer", () => {
    const character = createFixtureCharacter(1000);
    const next = setTalentChoice(character, "warden.strike", "not-a-real-option");
    expect(next).toBe(character);
  });
});

describe("computeLoadout (live describeLoadout summary)", () => {
  it("reports no changes for the fixture's untouched talents", () => {
    const character = createFixtureCharacter(1000);
    const { result, error } = computeLoadout(character);
    expect(error).toBeNull();
    expect(result?.log).toEqual([]);
  });

  it("reflects a talent change in the build summary", () => {
    const character = setTalentChoice(createFixtureCharacter(1000), "warden.strike", "warcry");
    const { result, error } = computeLoadout(character);
    expect(error).toBeNull();
    expect(result?.log.some((line) => line.includes("warden.strike"))).toBe(true);
  });

  it("reverting the talent change makes the summary empty again", () => {
    let character = setTalentChoice(createFixtureCharacter(1000), "warden.strike", "warcry");
    character = setTalentChoice(character, "warden.strike", null);
    const { result } = computeLoadout(character);
    expect(result?.log).toEqual([]);
  });
});
