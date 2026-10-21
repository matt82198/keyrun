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

  it("is a cast with a bar and a floor circle that spans the cast (BIBLE s3; f-dfafcec6)", () => {
    // The Lock deals no damage, so the dangerous-cast rule never covered it; it shipped with castTicks 0 and no
    // telegraph. Cast bars are the enemy's language: the boss's Lock is a cast bar and a floor circle that stays down
    // until the cast lands.
    const reg = loadContent();
    const lockAbility = reg.abilities.get("hollowWarden.lock")!;
    expect(lockAbility.castTicks).toBeGreaterThan(0);
    const boss = reg.enemies.get("hollowWarden.boss")!;
    const telegraph = boss.script.find((s) => s.cast === "hollowWarden.lock")?.telegraph;
    expect(telegraph?.shape).toBe("circle");
    expect(telegraph?.radius ?? 0).toBeGreaterThan(0);
    expect(telegraph?.leadTicks ?? 0).toBeGreaterThanOrEqual(lockAbility.castTicks);
    expect((telegraph as { spansCast?: boolean } | undefined)?.spansCast).toBe(true);
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
