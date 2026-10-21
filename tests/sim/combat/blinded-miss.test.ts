/**
 * D-ORACLE-BLIND-PARITY: the TS sim's port of the C# blind rule (game/sim/Keyrun.Sim/Combat/Mitigation.cs DealDamage,
 * IsBlinded). design/BIBLE.md section 4, Steam: "a cloud that blinds enemies inside (they miss)". An attacker carrying
 * an unexpired "blinded" instance (endsTick > now, the expireEffects rule) misses every damaging hit routed through
 * dealDamage: the target keeps its health, no absorb is consumed, and a "miss" event {sourceId, targetId, rawAmount}
 * replaces the "damage" event. Same cases as game/sim/Keyrun.Sim.Tests/CombatBlindedMissTests.cs, so both sims agree.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import { absorbPool, applyEffectInstance, type EffectDef } from "../../../sim/combat/effects";
import { dealDamage } from "../../../sim/combat/mitigation";
import type { ActorState, SimEvent } from "../../../sim/combat/types";

/** Mirrors content/data/effects/player.json "blinded": empty modifiers, the sim decides by id. */
const blinded: EffectDef = { id: "blinded", maxStacks: 1, dispellableTag: "magic", modifiers: {}, periodic: null };
const shield: EffectDef = { id: "shield", maxStacks: 1, dispellableTag: null, modifiers: { absorb: 50 }, periodic: null };
const defs: Record<string, EffectDef> = { blinded, shield };

function actor(id: number, kind: "player" | "enemy", maxHealth = 100): ActorState {
  return createActor({ id, kind, classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth, maxResource: 0 });
}

function blind(a: ActorState, durationTicks = 80, now = 0): ActorState {
  applyEffectInstance(a, blinded, 99, durationTicks, 1, now);
  return a;
}

function collect(): { events: SimEvent[]; emit: (e: SimEvent) => void } {
  const events: SimEvent[] = [];
  return { events, emit: (e) => events.push(e) };
}

describe("blinded attackers miss (C# Mitigation.DealDamage parity)", () => {
  it("a blinded attacker misses: the target keeps its health and a miss event replaces the damage event", () => {
    const src = blind(actor(1, "enemy"));
    const dst = actor(2, "player");
    const { events, emit } = collect();
    const r = dealDamage({ tick: 5 }, src, dst, 30, defs, emit, 1);
    expect(dst.health).toBe(100);
    expect(r).toEqual({ rawAmount: 30, modifiedAmount: 0, absorbed: 0, healthDamage: 0, died: false });
    expect(events).toEqual([{ tick: 5, type: "miss", sourceId: 1, targetId: 2, rawAmount: 30 }]);
    expect(Object.keys(events[0]!)).toEqual(["tick", "type", "sourceId", "targetId", "rawAmount"]);
  });

  it("a blinded attacker does not consume the target's absorb", () => {
    const src = blind(actor(1, "enemy"));
    const dst = actor(2, "player");
    applyEffectInstance(dst, shield, 3, 100, 1, 0);
    dealDamage({ tick: 5 }, src, dst, 30, defs, () => {}, 1);
    expect(absorbPool(dst, defs)).toBe(50);
  });

  it("an unblinded attacker still hits", () => {
    const dst = actor(2, "player");
    const { events, emit } = collect();
    dealDamage({ tick: 5 }, actor(1, "enemy"), dst, 30, defs, emit, 1);
    expect(dst.health).toBe(70);
    expect(events.map((e) => e.type)).toEqual(["damage"]);
  });

  it("a blinded target is still hit: blind only affects the attacker", () => {
    const dst = blind(actor(2, "enemy"));
    const { events, emit } = collect();
    dealDamage({ tick: 5 }, actor(1, "player"), dst, 30, defs, emit, 1);
    expect(dst.health).toBe(70);
    expect(events.map((e) => e.type)).toEqual(["damage"]);
  });

  it("blind lasts only while the status lasts: a hit on its end tick lands", () => {
    const src = blind(actor(1, "enemy"), 80, 0);
    const dst = actor(2, "player");
    dealDamage({ tick: 79 }, src, dst, 10, defs, () => {}, 1);
    expect(dst.health).toBe(100);
    dealDamage({ tick: 80 }, src, dst, 10, defs, () => {}, 1);
    expect(dst.health).toBe(90);
  });

  it("a missing source (a removed DoT caster) is never blinded: the hit lands", () => {
    const dst = actor(2, "player");
    dealDamage({ tick: 5 }, undefined, dst, 10, defs, () => {}, 1);
    expect(dst.health).toBe(90);
  });
});
