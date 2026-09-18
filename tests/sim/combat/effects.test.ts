/**
 * Tests for effect instance lifecycle and modifier aggregation (sim/combat/effects.ts).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import {
  aggregateModifiers,
  applyEffectInstance,
  consumeAbsorb,
  duePeriodicInstances,
  expireEffects,
  removeEffectInstances,
  type EffectDef,
} from "../../../sim/combat/effects";

function actor() {
  return createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource: 100 });
}

describe("applyEffectInstance", () => {
  it("adds a new instance with the requested stacks and duration", () => {
    const a = actor();
    const def: EffectDef = { id: "burn", maxStacks: 3, dispellableTag: "magic", modifiers: {}, periodic: null };
    applyEffectInstance(a, def, 2, 40, 1, 10);
    expect(a.effects).toHaveLength(1);
    expect(a.effects[0]).toMatchObject({ effectId: "burn", sourceId: 2, stacks: 1, endsTick: 50 });
  });

  it("stacks and refreshes an existing instance from the same source, capped at maxStacks", () => {
    const a = actor();
    const def: EffectDef = { id: "burn", maxStacks: 2, dispellableTag: null, modifiers: {}, periodic: null };
    applyEffectInstance(a, def, 2, 40, 1, 0);
    applyEffectInstance(a, def, 2, 40, 5, 10);
    expect(a.effects).toHaveLength(1);
    expect(a.effects[0]!.stacks).toBe(2);
    expect(a.effects[0]!.endsTick).toBe(50);
  });

  it("keeps separate instances per source", () => {
    const a = actor();
    const def: EffectDef = { id: "burn", maxStacks: 5, dispellableTag: null, modifiers: {}, periodic: null };
    applyEffectInstance(a, def, 2, 40, 1, 0);
    applyEffectInstance(a, def, 3, 40, 1, 0);
    expect(a.effects).toHaveLength(2);
  });
});

describe("removeEffectInstances / expireEffects", () => {
  it("removes all instances of an effect id", () => {
    const a = actor();
    const def: EffectDef = { id: "burn", maxStacks: 5, dispellableTag: null, modifiers: {}, periodic: null };
    applyEffectInstance(a, def, 2, 40, 1, 0);
    applyEffectInstance(a, def, 3, 40, 1, 0);
    removeEffectInstances(a, "burn");
    expect(a.effects).toHaveLength(0);
  });

  it("expires instances whose endsTick has passed, keeps ones still active", () => {
    const a = actor();
    const def: EffectDef = { id: "burn", maxStacks: 5, dispellableTag: null, modifiers: {}, periodic: null };
    applyEffectInstance(a, def, 2, 10, 1, 0);
    expireEffects(a, 9);
    expect(a.effects).toHaveLength(1);
    expireEffects(a, 10);
    expect(a.effects).toHaveLength(0);
  });
});

describe("duePeriodicInstances", () => {
  it("returns instances whose nextPeriodicTick has arrived", () => {
    const a = actor();
    const def: EffectDef = {
      id: "dot",
      maxStacks: 1,
      dispellableTag: null,
      modifiers: {},
      periodic: { everyTicks: 20, op: { op: "damage", amount: 10, school: "nature" } },
    };
    applyEffectInstance(a, def, 2, 100, 1, 0);
    expect(duePeriodicInstances(a, 19)).toHaveLength(0);
    expect(duePeriodicInstances(a, 20)).toHaveLength(1);
  });
});

describe("aggregateModifiers", () => {
  it("sums bp modifiers across stacks and multiple effects", () => {
    const a = actor();
    const haste: EffectDef = { id: "haste", maxStacks: 3, dispellableTag: null, modifiers: { hasteBp: 500 }, periodic: null };
    const slow: EffectDef = { id: "slow", maxStacks: 1, dispellableTag: null, modifiers: { movementBp: -3000 }, periodic: null };
    applyEffectInstance(a, haste, 2, 100, 2, 0);
    applyEffectInstance(a, slow, 2, 100, 1, 0);
    const totals = aggregateModifiers(a, { haste, slow });
    expect(totals.hasteBp).toBe(1000);
    expect(totals.movementBp).toBe(-3000);
    expect(totals.damageDoneBp).toBe(0);
  });
});

describe("consumeAbsorb", () => {
  it("consumes whole stacks to cover the request (a request needing part of a stack still spends it)", () => {
    const a = actor();
    const shield: EffectDef = { id: "shield", maxStacks: 3, dispellableTag: null, modifiers: { absorb: 50 }, periodic: null };
    applyEffectInstance(a, shield, 2, 100, 3, 0);
    const absorbed = consumeAbsorb(a, { shield }, 70); // needs 2 of 3 stacks (ceil(70/50))
    expect(absorbed).toBe(70);
    expect(a.effects[0]!.stacks).toBe(1);
  });

  it("removes the instance entirely once fully consumed", () => {
    const a = actor();
    const shield: EffectDef = { id: "shield", maxStacks: 1, dispellableTag: null, modifiers: { absorb: 50 }, periodic: null };
    applyEffectInstance(a, shield, 2, 100, 1, 0);
    const absorbed = consumeAbsorb(a, { shield }, 200);
    expect(absorbed).toBe(50);
    expect(a.effects).toHaveLength(0);
  });
});
