import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { toSimAbility, toSimEffectDef, toSimEffectOp } from "../../../client/src/sim-host/content-abilities.js";

const registry = loadContent();

describe("content-abilities: toSimEffectOp", () => {
  it("maps content's combo op to sim's tag-free payoff op", () => {
    expect(toSimEffectOp({ op: "combo", comboTable: "combos" }, "warden")).toEqual({ op: "payoff" });
  });

  it("injects the ability's owner class as a window op's setupClass", () => {
    expect(toSimEffectOp({ op: "window", windowId: "sundered", durationTicks: 60, onTarget: true }, "warden")).toEqual({
      op: "window",
      windowId: "sundered",
      durationTicks: 60,
      onTarget: true,
      setupClass: "warden",
    });
  });

  it("drops content's interrupt lockoutTicks (sim's lockout is RunContent-global)", () => {
    expect(toSimEffectOp({ op: "interrupt", lockoutTicks: 80 }, "warden")).toEqual({ op: "interrupt" });
  });

  it("drops content's resource name (sim has one resource per actor)", () => {
    expect(toSimEffectOp({ op: "resource", resource: "momentum", delta: 15 }, "warden")).toEqual({ op: "resource", delta: 15 });
  });

  it("defaults applyEffect.stacks to 1 when content omits it", () => {
    expect(toSimEffectOp({ op: "applyEffect", effectId: "stunned", durationTicks: 20 }, "warden")).toEqual({
      op: "applyEffect",
      effectId: "stunned",
      durationTicks: 20,
      stacks: 1,
    });
  });
});

describe("content-abilities: toSimAbility", () => {
  it("converts a real ability (warden.strike) end to end", () => {
    const strike = registry.abilities.get("warden.strike")!;
    const sim = toSimAbility(strike, "warden");
    expect(sim.id).toBe("warden.strike");
    // warden.strike carries no `cost` in content/data (a free builder) -- toSimAbility's default.
    expect(sim.cost).toEqual({ resource: "none", amount: 0 });
    expect(sim.effects).toEqual([
      { op: "damage", amount: 80, school: "physical" },
      { op: "resource", delta: 15 },
      { op: "window", windowId: "sundered", durationTicks: 60, onTarget: true, setupClass: "warden" },
    ]);
  });

  it("maps a line shape's width to sim's radius field (line half-width)", () => {
    const charge = registry.abilities.get("warden.charge")!;
    const sim = toSimAbility(charge, "warden");
    expect(sim.shape).toEqual({ kind: "line", length: 8000, radius: 1000 });
  });

  it("gives an ability with empty effects (cinder.blink) a harmless placeholder", () => {
    const blink = registry.abilities.get("cinder.blink")!;
    expect(blink.effects).toEqual([]);
    const sim = toSimAbility(blink, "cinder");
    expect(sim.effects).toEqual([{ op: "resource", delta: 0 }]);
  });

  it("uses Chanter's Mend variant (never Ward) for stance-differentiated abilities", () => {
    const cadence = registry.abilities.get("chanter.cadence")!;
    const sim = toSimAbility(cadence, "chanter");
    expect(sim.effects).toEqual([
      { op: "applyEffect", effectId: "mendingHot", durationTicks: 60, stacks: 1 },
      { op: "window", windowId: "attuned", durationTicks: 60, onTarget: true, setupClass: "chanter" },
    ]);
  });

  it("defaults cost to free when content omits it (e.g. warden.strike has no cost)", () => {
    const strike = registry.abilities.get("warden.strike")!;
    expect(strike.cost).toBeUndefined();
    expect(toSimAbility(strike, "warden").cost).toEqual({ resource: "none", amount: 0 });
  });
});

describe("content-abilities: toSimEffectDef", () => {
  it("converts a real effect (braced) end to end", () => {
    const braced = registry.effects.get("braced")!;
    expect(toSimEffectDef(braced)).toEqual({
      id: "braced",
      maxStacks: 1,
      dispellableTag: null,
      modifiers: { damageTakenBp: -1500 },
      periodic: null,
    });
  });

  it("carries a periodic op through (mendingHot)", () => {
    const mendingHot = registry.effects.get("mendingHot")!;
    const sim = toSimEffectDef(mendingHot);
    expect(sim.periodic).toEqual({ everyTicks: 20, op: { op: "heal", amount: 30 } });
  });
});
