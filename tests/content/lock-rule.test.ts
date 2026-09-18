/**
 * tests/content/lock-rule.test.ts
 * Usage: `npm test` (vitest). BIBLE.md Sec 9 "The Lock": the boss's self-Window accepts a Payoff
 * from a class other than whichever class's Setup last tagged it. Confirms the rule is both
 * encoded as data (EnemyDef.script[].windowRule) and correct as a function.
 */
import { describe, expect, it } from "vitest";
import { canPayoffCloseWindow, loadContent } from "../../content/index";

describe("The Lock alternation rule", () => {
  it("is encoded as data on the boss's lock script step", () => {
    const reg = loadContent();
    const boss = reg.enemies.get("hollowWarden.boss")!;
    const lockStep = boss.script.find((s) => s.cast === "hollowWarden.lock");
    expect(lockStep?.windowRule).toEqual({ windowId: "lock", kind: "alternate-setup-class" });
  });

  it("accepts any Payoff when the window has never been Setup-tagged", () => {
    expect(canPayoffCloseWindow("warden", null)).toBe(true);
  });

  it("accepts a Payoff from a different class than the last Setup", () => {
    expect(canPayoffCloseWindow("cinder", "warden")).toBe(true);
  });

  it("rejects a Payoff from the same class as the last Setup", () => {
    expect(canPayoffCloseWindow("warden", "warden")).toBe(false);
  });
});
