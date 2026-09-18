/**
 * Tests for GCD and charge-based cooldown tracking (sim/combat/cooldowns.ts).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import { chargesAvailable, consumeCharge, isAbilityReady, isGcdReady, startGcd } from "../../../sim/combat/cooldowns";
import type { AbilityDef } from "../../../sim/combat/abilities";

function ability(overrides: Partial<AbilityDef> = {}): AbilityDef {
  return {
    id: "kick",
    castTicks: 0,
    gcdTicks: 15,
    cooldownTicks: 40,
    charges: 2,
    cost: { resource: "energy", amount: 0 },
    range: 5000,
    shape: { kind: "target" },
    interruptible: false,
    effects: [{ op: "interrupt" }],
    ...overrides,
  };
}

describe("gcd", () => {
  it("is ready at tick 0 for a fresh actor and not ready right after starting", () => {
    const actor = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    expect(isGcdReady(actor, 0)).toBe(true);
    startGcd(actor, 0, 15);
    expect(isGcdReady(actor, 14)).toBe(false);
    expect(isGcdReady(actor, 15)).toBe(true);
  });
});

describe("charges", () => {
  it("starts with full charges and none on cooldown", () => {
    const actor = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const def = ability();
    expect(chargesAvailable(actor, def, 0)).toBe(2);
  });

  it("consumes one charge at a time and recharges after cooldownTicks", () => {
    const actor = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const def = ability();
    consumeCharge(actor, def, 0);
    expect(chargesAvailable(actor, def, 0)).toBe(1);
    expect(chargesAvailable(actor, def, 39)).toBe(1);
    expect(chargesAvailable(actor, def, 40)).toBe(2);
  });

  it("chains recharge timers when both charges are consumed: the second charge's recharge does not start until the first completes", () => {
    const actor = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const def = ability();
    consumeCharge(actor, def, 0);
    consumeCharge(actor, def, 5);
    expect(chargesAvailable(actor, def, 5)).toBe(0);
    expect(chargesAvailable(actor, def, 40)).toBe(1); // first charge back
    expect(chargesAvailable(actor, def, 79)).toBe(1); // second charge's 40-tick recharge chains from tick 40
    expect(chargesAvailable(actor, def, 80)).toBe(2); // second charge back at 40 + 40
  });

  it("treats cooldownTicks === 0 as always fully available", () => {
    const actor = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const def = ability({ cooldownTicks: 0, charges: 3 });
    consumeCharge(actor, def, 0);
    expect(chargesAvailable(actor, def, 0)).toBe(3);
  });
});

describe("isAbilityReady", () => {
  it("requires both gcd and a charge", () => {
    const actor = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const def = ability();
    startGcd(actor, 0, 15);
    expect(isAbilityReady(actor, def, 10)).toBe(false);
    expect(isAbilityReady(actor, def, 15)).toBe(true);
  });
});
