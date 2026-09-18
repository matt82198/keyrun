import { describe, expect, it } from "vitest";
import { spawnPull, checkPullAdvance, type DungeonDef } from "../../../sim/encounter/pulls";
import type { EncounterState } from "../../../sim/encounter/telegraphs";
import type { ActorState, SimEvent, SimState } from "../../../sim/encounter/types";

const dungeon: DungeonDef = {
  id: "hollow-vault",
  pulls: [
    {
      id: "choir",
      spawns: [
        { actorId: 101, classId: "melee_add", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 500 },
        { actorId: 102, classId: "caster_add", pos: { x: 1000, y: 0, z: 0 }, yaw: 0, health: 300 },
      ],
    },
    {
      id: "pair",
      spawns: [{ actorId: 201, classId: "melee_add", pos: { x: 0, y: 0, z: 5000 }, yaw: 0, health: 400 }],
    },
  ],
};

function baseEncounter(): EncounterState {
  return { telegraphs: [], breakBars: {}, pullIndex: 0, slackTicks: 0, parTicks: 0, keyLevel: 1, depleted: false };
}

function stateWithActors(actors: Record<number, ActorState>): SimState {
  return { tick: 0, seed: 1, prng: [1, 2, 3, 4], actors, runTimerTicks: 0, phase: "combat", log: [] };
}

function enemy(id: number, alive: boolean): ActorState {
  return {
    id,
    kind: "enemy",
    classId: "x",
    role: "none",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: alive ? 1 : 0,
    maxHealth: 500,
    resource: 0,
    maxResource: 0,
    alive,
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

describe("spawnPull", () => {
  it("spawns the pull's enemies at their defined points and emits pull_start", () => {
    const emitted: SimEvent[] = [];
    const spawned = spawnPull(dungeon, 0, 0, (e) => emitted.push(e));
    expect(spawned.map((a) => a.id)).toEqual([101, 102]);
    expect(spawned[0]?.pos).toEqual({ x: 0, y: 0, z: 0 });
    expect(spawned.every((a) => a.alive)).toBe(true);
    expect(emitted).toEqual([{ tick: 0, type: "pull_start", pullIndex: 0, pullId: "choir", actorIds: [101, 102] }]);
  });

  it("returns nothing for an out-of-range pull index", () => {
    expect(spawnPull(dungeon, 99, 0, () => {})).toEqual([]);
  });
});

describe("checkPullAdvance", () => {
  it("does not advance while any of the pull's enemies are alive", () => {
    const state = stateWithActors({ 101: enemy(101, true), 102: enemy(102, false) });
    const emitted: SimEvent[] = [];
    const next = checkPullAdvance(state, dungeon, baseEncounter(), 50, (e) => emitted.push(e));
    expect(next.pullIndex).toBe(0);
    expect(emitted).toHaveLength(0);
  });

  it("advances and emits pull_clear once every enemy in the pull is dead", () => {
    const state = stateWithActors({ 101: enemy(101, false), 102: enemy(102, false) });
    const emitted: SimEvent[] = [];
    const next = checkPullAdvance(state, dungeon, baseEncounter(), 50, (e) => emitted.push(e));
    expect(next.pullIndex).toBe(1);
    expect(emitted).toEqual([{ tick: 50, type: "pull_clear", pullIndex: 0, pullId: "choir" }]);
  });

  it("treats an enemy missing from state.actors as dead", () => {
    const state = stateWithActors({ 101: enemy(101, false) }); // 102 absent entirely
    const next = checkPullAdvance(state, dungeon, baseEncounter(), 50, () => {});
    expect(next.pullIndex).toBe(1);
  });
});
