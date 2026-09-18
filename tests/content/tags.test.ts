/**
 * tests/content/tags.test.ts
 * Usage: `npm test` (vitest). BIBLE.md Sec 5: "Each class has exactly two Setup abilities among
 * its five [rotational]... exactly two Payoff abilities." Confirms the tag data matches, using the
 * real sim's own "window:"/synthetic-effectId prefix convention (sim/combat/windows.ts's
 * WINDOW_PREFIX) for Setup abilities instead of the pre-integration "setup:" tag.
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "../../content/index";

const ROT_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;

describe("class rotational tag counts", () => {
  it("each class has exactly 2 setup + 2 payoff rotational abilities", () => {
    const reg = loadContent();
    for (const cls of reg.classes.values()) {
      const slots = cls.abilities as unknown as Record<string, string>;
      const rotAbilities = ROT_SLOTS.map((slot) => reg.abilities.get(slots[slot]!)!);
      const setups = rotAbilities.filter((a) => (a.tags ?? []).some((t) => t.startsWith("window:")));
      const payoffs = rotAbilities.filter((a) => (a.tags ?? []).includes("payoff"));
      expect(setups.length, `${cls.id} setup count`).toBe(2);
      expect(payoffs.length, `${cls.id} payoff count`).toBe(2);
    }
  });
});
