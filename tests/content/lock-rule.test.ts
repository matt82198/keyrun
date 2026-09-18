/**
 * tests/content/lock-rule.test.ts
 * Usage: `npm test` (vitest). BIBLE.md Sec 9 "The Lock": the boss's self-Window accepts a Payoff
 * from a class other than whichever class's Setup last tagged it. The real sim enforces this
 * entirely from the `alternate: true` flag on the boss's own "window" EffectOp
 * (sim/combat/abilities.ts / sim/combat/windows.ts) -- there is no separate EnemyDef.script-level
 * rule to encode; this suite confirms that flag is set and that `hollowWarden.boss`'s lock step
 * actually recurs under the real sim/encounter script semantics (a numeric `at` fires exactly
 * once; only `"loop"` repeats -- see sim/encounter/scripts.ts's `firesAt`).
 */
import { describe, expect, it } from "vitest";
import { canPayoffCloseWindow, loadContent } from "../../content/index";

describe("The Lock alternation rule", () => {
  it("is encoded as data on the boss's lock window op (alternate: true, self: true)", () => {
    const reg = loadContent();
    const lockAbility = reg.abilities.get("hollowWarden.lock")!;
    const windowOp = lockAbility.effects.find((e) => e.op === "window");
    expect(windowOp).toMatchObject({ windowId: "lock", setupClass: "boss", self: true, alternate: true });
  });

  it("the boss's lock script step recurs (real sim/encounter only repeats a \"loop\" step)", () => {
    const reg = loadContent();
    const boss = reg.enemies.get("hollowWarden.boss")!;
    const lockStep = boss.script.find((s) => s.cast === "hollowWarden.lock");
    expect(lockStep?.at).toBe("loop");
    expect(lockStep?.every).toBe(600);
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
