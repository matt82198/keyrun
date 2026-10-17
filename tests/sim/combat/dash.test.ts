/**
 * The additive "dash" command kind (Space, D10 camera-feel lane): a one-tick flat-arena burst of
 * DASH_DISTANCE_MM along `dir`, scaled by the same movement modifier as walking (so a root stops
 * it), then resolved by the ordinary collision + bounds clamp.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import { DASH_DISTANCE_MM } from "../../../sim/combat/movement";
import { validateCommand } from "../../../sim/core/commands";
import { step } from "../../../sim/core/tick";
import type { Command, SimState } from "../../../sim/combat/types";

function cfg(): RulesConfig {
  return {
    abilities: {},
    effects: { root: { id: "root", maxStacks: 1, dispellableTag: "magic", modifiers: { movementBp: -10000 }, periodic: null } },
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 },
  };
}

function stateWith(actor: ReturnType<typeof createActor>): SimState {
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: { [actor.id]: actor }, runTimerTicks: 0, phase: "combat", log: [] };
}

function player(): ReturnType<typeof createActor> {
  return createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
}

describe("dash command", () => {
  it("validates like move and rejects malformed input", () => {
    const ok = validateCommand({ tick: 3, actorId: 1, kind: "dash", dir: { x: 0, z: 1000 }, yaw: 0 });
    expect(ok).toEqual({ tick: 3, actorId: 1, kind: "dash", dir: { x: 0, z: 1000 }, yaw: 0 });
    expect(validateCommand({ tick: 3, actorId: 1, kind: "dash", dir: { x: 5000, z: 0 }, yaw: 0 })).toBeNull();
    expect(validateCommand({ tick: 3, actorId: 1, kind: "dash", dir: { x: 0, z: 1 }, yaw: 2000 })).toBeNull();
    expect(validateCommand({ tick: 3, actorId: 1, kind: "dash" })).toBeNull();
  });

  it("moves exactly DASH_DISTANCE_MM along dir in one tick and sets facing", () => {
    const a = player();
    const state = stateWith(a);
    const cmd: Command = { tick: 0, actorId: 1, kind: "dash", dir: { x: 600, z: 800 }, yaw: 100 };
    makeRules(cfg())(state, [cmd], () => {});
    expect(state.actors[1]!.pos).toEqual({ x: (DASH_DISTANCE_MM * 3) / 5, y: 0, z: (DASH_DISTANCE_MM * 4) / 5 });
    expect(state.actors[1]!.yaw).toBe(100);
    expect(DASH_DISTANCE_MM).toBeGreaterThan(300); // a burst, not a walk step
  });

  it("survives step()'s validate + sort and lands after the same tick's move", () => {
    const state = stateWith(player());
    const move: Command = { tick: 0, actorId: 1, kind: "move", dir: { x: 0, z: 1000 }, yaw: 0 };
    const dash: Command = { tick: 0, actorId: 1, kind: "dash", dir: { x: 1000, z: 0 }, yaw: 256 };
    const { state: next } = step(state, [dash, move], makeRules(cfg()));
    expect(next.actors[1]!.pos).toEqual({ x: DASH_DISTANCE_MM, y: 0, z: 300 });
    expect(next.actors[1]!.yaw).toBe(256); // dash applied after move, so its facing wins
  });

  it("is stopped by a root (movement modifier) and clamped by arena bounds", () => {
    const rooted = player();
    rooted.effects.push({ effectId: "root", sourceId: 1, stacks: 1, endsTick: 100, nextPeriodicTick: null });
    const s1 = stateWith(rooted);
    makeRules(cfg())(s1, [{ tick: 0, actorId: 1, kind: "dash", dir: { x: 1000, z: 0 }, yaw: 256 }], () => {});
    expect(s1.actors[1]!.pos).toEqual({ x: 0, y: 0, z: 0 });

    const edge = player();
    edge.pos = { x: 19000, y: 0, z: 0 };
    const s2 = stateWith(edge);
    makeRules(cfg())(s2, [{ tick: 0, actorId: 1, kind: "dash", dir: { x: 1000, z: 0 }, yaw: 256 }], () => {});
    expect(s2.actors[1]!.pos.x).toBe(20000);
  });
});
