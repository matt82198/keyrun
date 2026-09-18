/**
 * Unit tests for cast start/complete/stopcast/interrupt (sim/combat/casting.ts).
 * See interrupt-scenario.test.ts for the full tick-stepped interrupt-timing scenarios.
 */
import { describe, expect, it, vi } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { completeCast, interruptCast, startCast, stopcast, type CastContext } from "../../../sim/combat/casting";
import type { SimState } from "../../../sim/combat/types";

function makeState(tick: number, actors: ReturnType<typeof createActor>[]): SimState {
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

const fireball: AbilityDef = {
  id: "fireball",
  castTicks: 30,
  gcdTicks: 15,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "mana", amount: 20 },
  range: 30000,
  shape: { kind: "target" },
  interruptible: true,
  effects: [{ op: "damage", amount: 100, school: "fire" }],
};

const kick: AbilityDef = {
  id: "kick",
  castTicks: 0,
  gcdTicks: 15,
  cooldownTicks: 40,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "interrupt" }],
};

function ctx(): CastContext {
  return { abilities: { fireball, kick }, effects: {}, lockoutTicks: 60 };
}

describe("startCast", () => {
  it("fails when out of range", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 40000, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = makeState(0, [caster, target]);
    expect(startCast(state, caster, fireball, 2, undefined, 0)).toBe("out_of_range");
  });

  it("fails when the resource cost cannot be paid", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 10 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = makeState(0, [caster, target]);
    expect(startCast(state, caster, fireball, 2, undefined, 0)).toBe("insufficient_resource");
  });

  it("succeeds, spends resource, and sets castEndsTick = now + castTicks", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = makeState(10, [caster, target]);
    const err = startCast(state, caster, fireball, 2, undefined, 10);
    expect(err).toBeNull();
    expect(caster.resource).toBe(80);
    expect(caster.castId).toBe("fireball");
    expect(caster.castStartTick).toBe(10);
    expect(caster.castEndsTick).toBe(40);
  });

  it("fails while the ability's school is locked out", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    caster.effects.push({ effectId: "lockout:fire", sourceId: 2, stacks: 1, endsTick: 100, nextPeriodicTick: null });
    const state = makeState(10, [caster, target]);
    expect(startCast(state, caster, fireball, 2, undefined, 10)).toBe("school_locked");
  });
});

describe("completeCast", () => {
  it("applies the ability's effects to the caster's target and clears the cast", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = makeState(40, [caster, target]);
    startCast(state, caster, fireball, 2, undefined, 10);
    state.tick = 40;
    const emit = vi.fn();
    completeCast(state, caster, ctx(), emit);
    expect(target.health).toBe(0);
    expect(caster.castId).toBeNull();
    expect(emit).toHaveBeenCalledWith(expect.objectContaining({ type: "cast_complete", abilityId: "fireball" }));
  });
});

describe("stopcast", () => {
  it("clears the cast without applying any effects", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = makeState(10, [caster, target]);
    startCast(state, caster, fireball, 2, undefined, 10);
    const emit = vi.fn();
    stopcast(caster, emit, 20);
    expect(caster.castId).toBeNull();
    expect(target.health).toBe(100);
    expect(emit).toHaveBeenCalledWith(expect.objectContaining({ type: "cast_stopped" }));
  });
});

describe("interruptCast", () => {
  it("is a no-op against a non-interruptible ability", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = makeState(10, [caster, target]);
    const nonInterruptible: AbilityDef = { ...fireball, interruptible: false };
    const c: CastContext = { abilities: { fireball: nonInterruptible }, effects: {}, lockoutTicks: 60 };
    startCast(state, caster, nonInterruptible, 2, undefined, 10);
    const emit = vi.fn();
    interruptCast(state, caster, 2, c, emit);
    expect(caster.castId).toBe("fireball");
    expect(emit).not.toHaveBeenCalled();
  });

  it("cancels the cast and applies a school lockout when interruptible", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = makeState(10, [caster, target]);
    startCast(state, caster, fireball, 2, undefined, 10);
    state.tick = 25;
    const emit = vi.fn();
    interruptCast(state, caster, 2, ctx(), emit);
    expect(caster.castId).toBeNull();
    expect(caster.effects).toContainEqual(expect.objectContaining({ effectId: "lockout:fire", endsTick: 85 }));
    expect(emit).toHaveBeenCalledWith(expect.objectContaining({ type: "interrupt", school: "fire" }));
  });
});
