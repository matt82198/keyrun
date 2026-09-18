import { describe, expect, it } from "vitest";
import { stepEnemyScripts, type EnemyScript } from "../../../sim/encounter/scripts";
import type { ActorState, SimState } from "../../../sim/encounter/types";

function enemyActor(id: number): ActorState {
  return {
    id,
    kind: "enemy",
    classId: "boss",
    role: "none",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 1000,
    maxHealth: 1000,
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
  };
}

function stateAt(tick: number): SimState {
  return {
    tick,
    seed: 1,
    prng: [1, 2, 3, 4],
    actors: { 1: enemyActor(1) },
    runTimerTicks: tick,
    phase: "combat",
    log: [],
  };
}

describe("stepEnemyScripts", () => {
  const script: EnemyScript = [
    {
      at: "loop",
      every: 100,
      cast: "boss_cleave",
      telegraph: { shape: "cone", angle1024: 170, radius: 6000, leadTicks: 40 },
    },
  ];
  const scripts = { 1: script };

  it("fires the loop cast at every multiple of `every`, never at tick 0", () => {
    expect(stepEnemyScripts(stateAt(0), scripts).commands).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(99), scripts).commands).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(100), scripts).commands).toEqual([
      { tick: 100, actorId: 1, kind: "cast", abilityId: "boss_cleave" },
    ]);
    expect(stepEnemyScripts(stateAt(200), scripts).commands).toHaveLength(1);
  });

  it("emits the telegraph exactly leadTicks before the cast it precedes, with matching resolvesTick", () => {
    const before = stepEnemyScripts(stateAt(60), scripts); // 100 - 40
    expect(before.telegraphEvents).toHaveLength(1);
    expect(before.telegraphEvents[0]).toMatchObject({
      tick: 60,
      type: "telegraph",
      cast: "boss_cleave",
      resolvesTick: 100,
    });
    expect(before.commands).toHaveLength(0);

    const second = stepEnemyScripts(stateAt(160), scripts); // 200 - 40
    expect(second.telegraphEvents).toHaveLength(1);
    expect(second.telegraphEvents[0]?.resolvesTick).toBe(200);

    // one tick off in either direction: no telegraph
    expect(stepEnemyScripts(stateAt(59), scripts).telegraphEvents).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(61), scripts).telegraphEvents).toHaveLength(0);
  });

  it("never casts for a dead or missing actor", () => {
    const dead = stateAt(100);
    const actor = dead.actors[1];
    if (actor) actor.alive = false;
    expect(stepEnemyScripts(dead, scripts).commands).toHaveLength(0);

    const missing: SimState = { ...stateAt(100), actors: {} };
    expect(stepEnemyScripts(missing, scripts).commands).toHaveLength(0);
  });

  it("fires a one-shot numeric `at` step exactly once, at that tick", () => {
    const oneShot: EnemyScript = [{ at: 250, cast: "intro_roar" }];
    const scripts2 = { 1: oneShot };
    expect(stepEnemyScripts(stateAt(249), scripts2).commands).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(250), scripts2).commands).toHaveLength(1);
    expect(stepEnemyScripts(stateAt(251), scripts2).commands).toHaveLength(0);
  });
});
