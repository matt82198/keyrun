/**
 * Tests for the integer damage/heal pipeline (sim/combat/damage.ts).
 */
import { describe, expect, it, vi } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import { applyDamage, applyHeal, applyModifiers } from "../../../sim/combat/damage";
import { applyEffectInstance, type EffectDef } from "../../../sim/combat/effects";

function target(health = 100) {
  const a = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: health, maxResource: 0 });
  a.health = health;
  return a;
}

describe("applyModifiers", () => {
  it("passes base amount through with zero bp modifiers", () => {
    expect(applyModifiers(100, 0, 0)).toBe(100);
  });

  it("applies +20% source and -10% target multiplicatively, truncating toward zero", () => {
    // 100 * 1.20 = 120; 120 * 0.90 = 108
    expect(applyModifiers(100, 2000, -1000)).toBe(108);
  });

  it("never goes negative even with large negative modifiers", () => {
    expect(applyModifiers(100, 0, -20000)).toBe(0);
  });
});

describe("applyDamage", () => {
  it("reduces health by the modified amount and floors at 0", () => {
    const t = target(50);
    const emit = vi.fn();
    const result = applyDamage({ tick: 5 }, t, 80, 0, 0, {}, emit, 1);
    expect(result.healthDamage).toBe(50);
    expect(t.health).toBe(0);
    expect(t.alive).toBe(false);
  });

  it("emits a damage event and a death event exactly once on lethal damage", () => {
    const t = target(10);
    const emit = vi.fn();
    applyDamage({ tick: 5 }, t, 10, 0, 0, {}, emit, 1);
    expect(emit).toHaveBeenCalledTimes(2);
    expect(emit).toHaveBeenNthCalledWith(1, expect.objectContaining({ type: "damage" }));
    expect(emit).toHaveBeenNthCalledWith(2, expect.objectContaining({ type: "death", actorId: 2 }));
  });

  it("does not re-emit death (or any event) on further damage to an already-dead actor", () => {
    const t = target(10);
    const emit = vi.fn();
    applyDamage({ tick: 5 }, t, 10, 0, 0, {}, emit, 1);
    emit.mockClear();
    applyDamage({ tick: 6 }, t, 10, 0, 0, {}, emit, 1);
    // A dead actor is inert: no damage event either (tests/sim/combat/dead-actors.test.ts).
    expect(emit).not.toHaveBeenCalled();
  });

  it("consumes absorb before health, in pipeline order base->mods->absorb->health", () => {
    const t = target(100);
    const shield: EffectDef = { id: "shield", maxStacks: 1, dispellableTag: null, modifiers: { absorb: 30 }, periodic: null };
    applyEffectInstance(t, shield, 1, 100, 1, 0);
    const emit = vi.fn();
    const result = applyDamage({ tick: 0 }, t, 50, 0, 0, { shield }, emit, 1);
    expect(result.absorbed).toBe(30);
    expect(result.healthDamage).toBe(20);
    expect(t.health).toBe(80);
  });
});

describe("applyHeal", () => {
  it("caps healing at maxHealth", () => {
    const t = target(100);
    t.health = 90;
    const emit = vi.fn();
    const applied = applyHeal({ tick: 0 }, t, 50, 1, emit);
    expect(applied).toBe(10);
    expect(t.health).toBe(100);
  });
});
