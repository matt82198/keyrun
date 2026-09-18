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

  it("fires a numeric `at` + `every` step at at, at+every, at+2*every, ...", () => {
    const offsetLoop: EnemyScript = [{ at: 600, every: 400, cast: "pair.stackCall" }];
    const scripts3 = { 1: offsetLoop };
    expect(stepEnemyScripts(stateAt(200), scripts3).commands).toHaveLength(0);
    expect(stepEnemyScripts(stateAt(400), scripts3).commands).toHaveLength(0); // every alone would fire here; offset must suppress it
    expect(stepEnemyScripts(stateAt(600), scripts3).commands).toEqual([
      { tick: 600, actorId: 1, kind: "cast", abilityId: "pair.stackCall" },
    ]);
    expect(stepEnemyScripts(stateAt(1000), scripts3).commands).toHaveLength(1);
    expect(stepEnemyScripts(stateAt(1400), scripts3).commands).toHaveLength(1);
    expect(stepEnemyScripts(stateAt(800), scripts3).commands).toHaveLength(0);
  });

  describe("onDeath", () => {
    const deathScript: EnemyScript = [{ at: "onDeath", cast: "seed.deathBloom" }];
    const deathScripts = { 1: deathScript };

    it("never fires while the actor is alive", () => {
      const alive = stateAt(500);
      expect(stepEnemyScripts(alive, deathScripts).commands).toHaveLength(0);
    });

    it("fires once the actor is dead", () => {
      const dead = stateAt(500);
      const actor = dead.actors[1];
      if (actor) actor.alive = false;
      const result = stepEnemyScripts(dead, deathScripts);
      expect(result.commands).toEqual([{ tick: 500, actorId: 1, kind: "cast", abilityId: "seed.deathBloom" }]);
      expect(result.firedOnDeath[1]).toEqual(["seed.deathBloom"]);
    });

    it("does not re-fire once `firedOnDeath` records it (the caller's fire-once contract)", () => {
      const dead = stateAt(600);
      const actor = dead.actors[1];
      if (actor) actor.alive = false;
      const result = stepEnemyScripts(dead, deathScripts, { 1: ["seed.deathBloom"] });
      expect(result.commands).toHaveLength(0);
      expect(result.firedOnDeath[1]).toEqual(["seed.deathBloom"]);
    });

    it("emits the telegraph on the same tick the onDeath cast fires, resolving leadTicks later", () => {
      const withTelegraph: EnemyScript = [
        {
          at: "onDeath",
          cast: "seed.deathBloom",
          telegraph: { shape: "circle", radius: 2000, leadTicks: 20 },
        },
      ];
      const dead = stateAt(700);
      const actor = dead.actors[1];
      if (actor) actor.alive = false;
      const result = stepEnemyScripts(dead, { 1: withTelegraph });
      expect(result.telegraphEvents).toHaveLength(1);
      expect(result.telegraphEvents[0]).toMatchObject({
        tick: 700,
        type: "telegraph",
        cast: "seed.deathBloom",
        resolvesTick: 720,
      });
    });

    it("leaves loop/numeric steps for the same actor unaffected by a dead actor's onDeath step", () => {
      const mixed: EnemyScript = [
        { at: "loop", every: 100, cast: "seed.slam" },
        { at: "onDeath", cast: "seed.deathBloom" },
      ];
      const dead = stateAt(500);
      const actor = dead.actors[1];
      if (actor) actor.alive = false;
      const result = stepEnemyScripts(dead, { 1: mixed });
      expect(result.commands).toEqual([{ tick: 500, actorId: 1, kind: "cast", abilityId: "seed.deathBloom" }]);
    });
  });
});
