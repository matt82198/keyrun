/**
 * tests/bots/kits.test.ts
 *
 * bots/src/kits.ts: ClassKit built from the real content/ registries (design/BIBLE.md Sec 5's
 * three starter classes), plus resolveKit()'s fallback to a scripted BotView.classKit when
 * `classId` isn't one of them.
 *
 * Usage: `npx vitest run tests/bots/kits.test.ts`
 */
import { describe, expect, it } from "vitest";
import { getClassKit, resolveKit } from "@bots/src/kits.js";

describe("getClassKit", () => {
  it("returns null for a classId content doesn't define", () => {
    expect(getClassKit("priest")).toBeNull();
  });

  it("builds Warden's kit: two Setup, two Payoff, the real interrupt/defensive/movement ids, no dispel", () => {
    const kit = getClassKit("warden");
    expect(kit).not.toBeNull();
    expect(kit?.loopKind).toBe("builder-spender");
    expect(kit?.setupAbilityIds).toEqual(["warden.strike", "warden.bulwark"]);
    expect(kit?.payoffAbilityIds).toEqual(["warden.guardBreak", "warden.reckoning"]);
    expect(kit?.windowIdBySetupAbility["warden.strike"]).toBe("sundered");
    expect(kit?.windowIdBySetupAbility["warden.bulwark"]).toBe("bulwark");
    expect(kit?.interruptAbilityId).toBe("warden.shieldBash");
    expect(kit?.defensiveAbilityId).toBe("warden.brace");
    expect(kit?.movementAbilityId).toBe("warden.charge");
    expect(kit?.dispelAbilityId).toBeNull();
    expect(kit?.selfSustainAbilityId).toBe("warden.vigil");
  });

  it("builds Cinder's kit: Cold Snap is the sole resource dump, Purge is the dispel", () => {
    const kit = getClassKit("cinder");
    expect(kit?.loopKind).toBe("heat-overheat");
    expect(kit?.resourceDumpAbilityIds).toEqual(["cinder.coldSnap"]);
    expect(kit?.dispelAbilityId).toBe("cinder.purge");
    expect(kit?.costByAbility["cinder.detonate"]).toBe(30);
    expect(kit?.resourceGainByAbility["cinder.coldSnap"]).toBe(-100);
  });

  it("builds Chanter's kit: Refrain swaps the stance resource and is the heal-triage ability", () => {
    const kit = getClassKit("chanter");
    expect(kit?.loopKind).toBe("stance");
    expect(kit?.stanceSwapAbilityId).toBe("chanter.refrain");
    expect(kit?.healAbilityId).toBe("chanter.refrain");
    expect(kit?.dispelAbilityId).toBe("chanter.cleanse");
  });

  it("rotation priority: a resource-cost finisher outranks a Payoff, which outranks a Setup, which outranks filler", () => {
    const kit = getClassKit("cinder");
    const rot = kit?.rotation ?? [];
    const at = (id: string) => rot.indexOf(id);
    expect(at("cinder.detonate")).toBeLessThan(at("cinder.coldSnap")); // cost finisher before Payoff-dump
    expect(at("cinder.coldSnap")).toBeLessThan(at("cinder.flare")); // Payoff before Setup
    expect(at("cinder.flare")).toBeLessThan(at("cinder.emberBolt")); // Setup before filler
  });

  it("memoises: repeated calls for the same classId return the identical object", () => {
    expect(getClassKit("warden")).toBe(getClassKit("warden"));
  });
});

describe("resolveKit", () => {
  it("prefers content's real kit when classId matches a starter class", () => {
    const kit = resolveKit("warden", { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null });
    expect(kit.interruptAbilityId).toBe("warden.shieldBash");
    expect(kit.setupAbilityIds).toEqual(["warden.strike", "warden.bulwark"]);
  });

  it("falls back to BotView.classKit's own fields for a scripted-test placeholder classId", () => {
    const kit = resolveKit("warrior", { rotation: ["filler"], interruptAbilityId: "kick", defensiveAbilityId: "wall", healAbilityId: null });
    expect(kit.rotation).toEqual(["filler"]);
    expect(kit.interruptAbilityId).toBe("kick");
    expect(kit.defensiveAbilityId).toBe("wall");
    expect(kit.setupAbilityIds).toEqual([]);
    expect(kit.payoffAbilityIds).toEqual([]);
    expect(kit.loopKind).toBeNull();
  });
});
