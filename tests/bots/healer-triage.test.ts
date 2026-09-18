/**
 * tests/bots/healer-triage.test.ts
 *
 * Healer role: a dispellable ally is treated first, then the lowest-health
 * ally under the 70% threshold is healed ahead of allies above it.
 *
 * Usage: `npx vitest run tests/bots/healer-triage.test.ts`
 */
import { describe, expect, it } from "vitest";
import { healerDecide } from "@bots/src/roles.js";
import type { ActorState, BotView, SimState } from "@bots/src/types.js";

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1,
    kind: "player",
    classId: "priest",
    role: "healer",
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
    classKit: { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: "flash-heal" },
  };
}

describe("healer triage", () => {
  it("picks the lowest-health ally under 70% health over a higher one", () => {
    const healer = makeActor({ id: 1, role: "healer" });
    const tank = makeActor({ id: 2, role: "tank", health: 40, maxHealth: 100, pos: { x: 1, y: 0, z: 0 } });
    const dps = makeActor({ id: 3, role: "damage", health: 65, maxHealth: 100, pos: { x: 1, y: 0, z: 0 } });
    const view = makeView({ [healer.id]: healer, [tank.id]: tank, [dps.id]: dps });

    const commands = healerDecide(view, healer);
    expect(commands[0]).toMatchObject({ kind: "cast", abilityId: "flash-heal", targetId: tank.id });
  });

  it("does not heal allies at or above the 70% threshold", () => {
    const healer = makeActor({ id: 1, role: "healer" });
    const dps = makeActor({ id: 3, role: "damage", health: 90, maxHealth: 100, pos: { x: 1, y: 0, z: 0 } });
    const view = makeView({ [healer.id]: healer, [dps.id]: dps });

    expect(healerDecide(view, healer)).toEqual([]);
  });

  it("dispels a debuff carrier before healing a lower-health ally", () => {
    const healer = makeActor({ id: 1, role: "healer" });
    const tank = makeActor({ id: 2, role: "tank", health: 40, maxHealth: 100, pos: { x: 1, y: 0, z: 0 } });
    const dps = makeActor({
      id: 3,
      role: "damage",
      health: 90,
      maxHealth: 100,
      pos: { x: 1, y: 0, z: 0 },
      effects: [{ effectId: "dispellable:poison", sourceId: 100, stacks: 1, endsTick: 50, nextPeriodicTick: null }],
    });
    const view = makeView({ [healer.id]: healer, [tank.id]: tank, [dps.id]: dps });

    const commands = healerDecide(view, healer);
    expect(commands[0]).toMatchObject({ kind: "cast", abilityId: "dispel", targetId: dps.id });
  });
});
