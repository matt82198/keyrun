/**
 * tests/bots/tank-facing.test.ts
 *
 * Tank role: acquires the enemy with the largest threat deficit, and turns
 * to face away from the party centroid (so cleave/frontal danger points off
 * the raid). Also fires its defensive cooldown under the 35% health floor.
 *
 * Usage: `npx vitest run tests/bots/tank-facing.test.ts`
 */
import { describe, expect, it } from "vitest";
import { tankDecide } from "@bots/src/roles.js";
import type { ActorState, BotView, SimState } from "@bots/src/types.js";

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1,
    kind: "player",
    classId: "warrior",
    role: "tank",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 100,
    maxHealth: 100,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId: null,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
    ...overrides,
  };
}

function makeView(actors: Record<number, ActorState>): BotView {
  const state: SimState = {
    tick: 10,
    seed: 1,
    prng: [1, 2, 3, 4],
    actors,
    runTimerTicks: 0,
    phase: "combat",
    log: [],
  };
  return {
    state,
    telegraphs: [],
    classKit: { rotation: [], interruptAbilityId: null, defensiveAbilityId: "shield-wall", healAbilityId: null },
  };
}

describe("tank state machine", () => {
  it("acquires the enemy with the highest threat deficit", () => {
    const tank = makeActor({ id: 1, pos: { x: 0, y: 0, z: -5 } });
    const offTarget = makeActor({ id: 10, kind: "enemy", threat: { 1: 10, 3: 50 } });
    const onTarget = makeActor({ id: 11, kind: "enemy", threat: { 1: 100 } });
    const view = makeView({ [tank.id]: tank, [offTarget.id]: offTarget, [onTarget.id]: onTarget });

    const commands = tankDecide(view, tank);
    expect(commands.some((c) => c.kind === "target" && c.targetId === offTarget.id)).toBe(true);
  });

  it("turns to face away from the party centroid", () => {
    const tank = makeActor({ id: 1, pos: { x: 0, y: 0, z: -5 }, yaw: 0 });
    const ally = makeActor({ id: 2, role: "damage", pos: { x: 0, y: 0, z: 0 } });
    const view = makeView({ [tank.id]: tank, [ally.id]: ally });

    const move = tankDecide(view, tank).find((c) => c.kind === "move");
    expect(move).toBeDefined();
    if (move && move.kind === "move") {
      // Party is at z=0, tank at z=-5: facing away means yaw points further negative-z, i.e. atan2(-1,0) = -PI/2.
      expect(move.yaw).toBeCloseTo(-Math.PI / 2, 5);
    }
  });

  it("pops its defensive under the 35% health floor", () => {
    const tank = makeActor({ id: 1, health: 30, maxHealth: 100 });
    const view = makeView({ [tank.id]: tank });

    const commands = tankDecide(view, tank);
    expect(commands.some((c) => c.kind === "cast" && c.abilityId === "shield-wall")).toBe(true);
  });

  it("does not pop its defensive above the health floor", () => {
    const tank = makeActor({ id: 1, health: 80, maxHealth: 100 });
    const view = makeView({ [tank.id]: tank });

    const commands = tankDecide(view, tank);
    expect(commands.some((c) => c.kind === "cast")).toBe(false);
  });
});
