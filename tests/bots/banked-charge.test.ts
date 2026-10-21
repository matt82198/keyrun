/**
 * tests/bots/banked-charge.test.ts
 *
 * BANKED-CHARGE (ruling R-PILE-7c, finding f-f44791bc). Twin of
 * game/bots/Keyrun.Bots.Tests/BankedChargeReadinessTests.cs: a multi-charge ability is castable while one charge is
 * banked, in the sim (sim/combat/cooldowns.ts chargesAvailable: cooldowns[id] is the tick the recharging charge
 * finishes) and so in bot readiness (abilityReady with the kit's chargedAbilities). A one-charge ability still waits for
 * cooldowns[id].
 *
 * Usage: `npx vitest run tests/bots/banked-charge.test.ts`
 */
import "./real-content-setup.js";
import { describe, expect, it } from "vitest";
import { abilityReady } from "@bots/src/roles/shared.js";
import { getClassKit, resolveKit } from "@bots/src/kits.js";
import type { ActorState } from "@bots/src/types.js";

const NO_VIEW_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 2, kind: "player", classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 100,
    maxHealth: 100, resource: 0, maxResource: 0, alive: true, targetId: null, castId: null, castEndsTick: null,
    castStartTick: null, cooldowns: {}, gcdReadyTick: 0, effects: [], threat: {}, ...overrides,
  };
}

describe("banked charge readiness (R-PILE-7c)", () => {
  it("sees the banked second charge of a two-charge ability (Encore's Chorus: 2 charges, cooldown 100)", () => {
    const kit = { chargedAbilities: { "chanter.chorus": { charges: 2, cooldownTicks: 100 } } };
    // One press from full at 692: the recharging charge finishes at 792, the second is banked.
    expect(abilityReady(makeActor({ cooldowns: { "chanter.chorus": 792 } }), "chanter.chorus", 700, kit)).toBe(true);
    // Two presses at 692: no charge until 792.
    expect(abilityReady(makeActor({ cooldowns: { "chanter.chorus": 892 } }), "chanter.chorus", 700, kit)).toBe(false);
    expect(abilityReady(makeActor({ cooldowns: { "chanter.chorus": 892 } }), "chanter.chorus", 792, kit)).toBe(true);
  });

  it("keeps a one-charge ability waiting for its cooldown", () => {
    const kit = resolveKit("chanter", NO_VIEW_KIT);
    expect(kit.chargedAbilities?.["chanter.chorus"]).toBeUndefined();
    expect(abilityReady(makeActor({ cooldowns: { "chanter.chorus": 812 } }), "chanter.chorus", 700, kit)).toBe(false);
    expect(abilityReady(makeActor({ cooldowns: { "chanter.chorus": 812 } }), "chanter.chorus", 812, kit)).toBe(true);
  });

  it("builds the content two-charge movement ability into the kit and counts its banked charge", () => {
    const classKit = getClassKit("chanter");
    expect(classKit?.chargedAbilities).toEqual({ "chanter.glide": { charges: 2, cooldownTicks: 200 } });
    const kit = resolveKit("chanter", NO_VIEW_KIT);
    expect(abilityReady(makeActor({ cooldowns: { "chanter.glide": 300 } }), "chanter.glide", 200, kit)).toBe(true);
    expect(abilityReady(makeActor({ cooldowns: { "chanter.glide": 500 } }), "chanter.glide", 200, kit)).toBe(false);
  });
});
