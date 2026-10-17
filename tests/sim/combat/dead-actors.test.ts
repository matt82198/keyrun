/**
 * Dead actors are inert to heal/damage and invisible to target/shape resolution.
 *
 * Defect: applyHeal/applyDamage (sim/combat/damage.ts) never checked `alive`, so a heal on a
 * dead actor raised its health while it stayed `alive=false` and a hit on a corpse emitted a
 * damage event; resolveTargets (sim/combat/targeting.ts) returned dead actors for circle, cone
 * and line shapes (and for a "target" shape whose target died mid-cast), so corpses soaked AoE
 * and dead party members climbed back to 120-250 HP in pull traces.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef, AbilityShape, EffectOp } from "../../../sim/combat/abilities";
import { completeCast, startCast, type CastContext } from "../../../sim/combat/casting";
import { applyDamage, applyHeal } from "../../../sim/combat/damage";
import { absorbPool, applyEffectInstance, type EffectDef } from "../../../sim/combat/effects";
import { resolveTargets } from "../../../sim/combat/targeting";
import type { ActorState, SimEvent, SimState } from "../../../sim/combat/types";

function actor(id: number, kind: "player" | "enemy", x: number, maxHealth = 100): ActorState {
  return createActor({ id, kind, classId: "x", role: "none", pos: { x, y: 0, z: 0 }, yaw: 0, maxHealth, maxResource: 100 });
}

function kill(a: ActorState): void {
  applyDamage({ tick: 1 }, a, a.health, 0, 0, {}, () => {}, 99);
}

function makeState(actors: ActorState[]): SimState {
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

function def(shape: AbilityShape, effects: EffectOp[], castTicks = 0): AbilityDef {
  return {
    id: "t", castTicks, gcdTicks: 0, cooldownTicks: 0, charges: 1,
    cost: { resource: "energy", amount: 0 }, range: 5000, shape, interruptible: false, effects,
  };
}

const dmg: EffectOp = { op: "damage", amount: 10, school: "physical" };
const heal: EffectOp = { op: "heal", amount: 10 };

describe("applyHeal on a dead actor", () => {
  it("leaves hp at 0, keeps alive=false, returns 0 and emits no heal event", () => {
    const a = actor(2, "player", 0);
    kill(a);
    const events: SimEvent[] = [];
    const applied = applyHeal({ tick: 2 }, a, 40, 1, (e) => events.push(e));
    expect(applied).toBe(0);
    expect(a.health).toBe(0);
    expect(a.alive).toBe(false);
    expect(events).toEqual([]);
  });
});

describe("applyDamage on a dead actor", () => {
  it("is a no-op: hp stays 0, no damage or death event, absorb untouched, died=false", () => {
    const shieldDef: EffectDef = { id: "shield", maxStacks: 1, dispellableTag: null, modifiers: { absorb: 50 }, periodic: null };
    const a = actor(2, "enemy", 0, 10);
    kill(a);
    applyEffectInstance(a, shieldDef, 1, 100, 1, 1);
    const events: SimEvent[] = [];
    const r = applyDamage({ tick: 2 }, a, 30, 0, 0, { shield: shieldDef }, (e) => events.push(e), 1);
    expect(a.health).toBe(0);
    expect(a.alive).toBe(false);
    expect(r.died).toBe(false);
    expect(r.healthDamage).toBe(0);
    expect(r.absorbed).toBe(0);
    expect(events).toEqual([]);
    expect(absorbPool(a, { shield: shieldDef })).toBe(50);
  });
});

describe("resolveTargets excludes dead actors", () => {
  it("circle (hostile) skips a dead enemy in radius", () => {
    const caster = actor(1, "player", 0);
    const live = actor(2, "enemy", 50);
    const dead = actor(3, "enemy", 60);
    kill(dead);
    const state = makeState([caster, live, dead]);
    expect(resolveTargets(state, caster, def({ kind: "circle", radius: 100 }, [dmg])).map((a) => a.id)).toEqual([2]);
  });

  it("circle (friendly) skips a dead ally in radius", () => {
    const caster = actor(1, "player", 0);
    const live = actor(2, "player", 50);
    const dead = actor(3, "player", 60);
    kill(dead);
    const state = makeState([caster, live, dead]);
    expect(resolveTargets(state, caster, def({ kind: "circle", radius: 100 }, [heal])).map((a) => a.id)).toEqual([1, 2]);
  });

  it("circle (friendly) does not add a dead caster to its own heal", () => {
    const caster = actor(1, "player", 0);
    const live = actor(2, "player", 50);
    kill(caster);
    const state = makeState([caster, live]);
    expect(resolveTargets(state, caster, def({ kind: "circle", radius: 100 }, [heal])).map((a) => a.id)).toEqual([2]);
  });

  it("cone skips a dead enemy ahead", () => {
    const caster = actor(1, "player", 0);
    const live = actor(2, "enemy", 100);
    const dead = actor(3, "enemy", 150);
    kill(dead);
    const state = makeState([caster, live, dead]);
    expect(resolveTargets(state, caster, def({ kind: "cone", angle1024: 256, radius: 200 }, [dmg])).map((a) => a.id)).toEqual([2]);
  });

  it("line skips a dead enemy ahead", () => {
    const caster = actor(1, "player", 0);
    const live = actor(2, "enemy", 100);
    const dead = actor(3, "enemy", 150);
    kill(dead);
    const state = makeState([caster, live, dead]);
    expect(resolveTargets(state, caster, def({ kind: "line", length: 300, radius: 50 }, [dmg])).map((a) => a.id)).toEqual([2]);
  });

  it("target shape resolves to nothing when the target is dead", () => {
    const caster = actor(1, "player", 0);
    const dead = actor(2, "enemy", 100);
    kill(dead);
    caster.targetId = 2;
    const state = makeState([caster, dead]);
    expect(resolveTargets(state, caster, def({ kind: "target" }, [dmg]))).toEqual([]);
  });
});

describe("single-target cast on a dead target", () => {
  it("startCast already refuses with invalid_target", () => {
    const caster = actor(1, "player", 0);
    const dead = actor(2, "enemy", 100);
    kill(dead);
    const state = makeState([caster, dead]);
    expect(startCast(state, caster, def({ kind: "target" }, [dmg]), 2, undefined, 0)).toBe("invalid_target");
  });

  it("a target that dies mid-cast receives no damage/heal events when the cast completes", () => {
    const caster = actor(1, "player", 0);
    const t = actor(2, "enemy", 100);
    const d = def({ kind: "target" }, [dmg], 10);
    const state = makeState([caster, t]);
    expect(startCast(state, caster, d, 2, undefined, 0)).toBeNull();
    kill(t);
    const events: SimEvent[] = [];
    const ctx: CastContext = { abilities: { t: d }, effects: {}, lockoutTicks: 0 };
    completeCast(state, caster, ctx, (e) => events.push(e));
    expect(events.filter((e) => e.type === "damage" || e.type === "death")).toEqual([]);
    expect(t.health).toBe(0);
  });
});
