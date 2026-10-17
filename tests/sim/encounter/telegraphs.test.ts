import { describe, expect, it } from "vitest";
import {
  pointInCircle,
  pointInCone,
  pointInLine,
  resolveTelegraphs,
  type EncounterState,
  type Telegraph,
} from "../../../sim/encounter/telegraphs";
import { defaultEncounterRules } from "../../../sim/encounter/clauses";
import type { ActorState, SimState } from "../../../sim/encounter/types";

function playerActor(id: number, pos: { x: number; y: number; z: number }): ActorState {
  return {
    id,
    kind: "player",
    classId: "warden",
    role: "tank",
    pos,
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
  };
}

function baseEncounter(telegraphs: Telegraph[]): EncounterState {
  return {
    telegraphs,
    breakBars: {},
    pullIndex: 0,
    slackTicks: 0,
    parTicks: 0,
    keyLevel: 1,
    depleted: false,
    rules: defaultEncounterRules(1),
  };
}

const ORIGIN = { x: 0, y: 0, z: 0 };

describe("integer geometry", () => {
  it("point-in-circle", () => {
    expect(pointInCircle(ORIGIN, 5000, { x: 3000, y: 0, z: 4000 })).toBe(true); // exactly on radius (3-4-5)
    expect(pointInCircle(ORIGIN, 5000, { x: 3000, y: 0, z: 4001 })).toBe(false);
  });

  // Facing convention = sim/combat/targeting.ts faceDir: yaw 0 faces +x, yaw 256 faces +z.
  it("point-in-cone: directly in front of yaw 0 (facing +x) is true, directly behind is false", () => {
    const front = { x: 3000, y: 0, z: 0 };
    const behind = { x: -3000, y: 0, z: 0 };
    expect(pointInCone(ORIGIN, 0, 200, 6000, front)).toBe(true);
    expect(pointInCone(ORIGIN, 0, 200, 6000, behind)).toBe(false);
  });

  it("point-in-cone: respects the facing yaw, not just world +x", () => {
    // yaw 256 faces +z; a point on +z is in front, a point on +x (was "front" at yaw 0) is now to the side/out.
    const onPlusZ = { x: 0, y: 0, z: 3000 };
    const onPlusX = { x: 3000, y: 0, z: 0 };
    expect(pointInCone(ORIGIN, 256, 200, 6000, onPlusZ)).toBe(true);
    expect(pointInCone(ORIGIN, 256, 40, 6000, onPlusX)).toBe(false); // narrow cone, 90deg off yaw
  });

  it("point-in-cone: outside the radius is never a hit even if angularly centered", () => {
    expect(pointInCone(ORIGIN, 0, 200, 2000, { x: 3000, y: 0, z: 0 })).toBe(false);
  });

  it("point-in-line: width math includes points within half-width, excludes beyond it", () => {
    const withinWidth = { x: 2500, y: 0, z: 400 }; // width 1000 -> half-width 500
    const beyondWidth = { x: 2500, y: 0, z: 600 };
    const onAxis = { x: 2500, y: 0, z: 0 };
    expect(pointInLine(ORIGIN, 0, 5000, 1000, onAxis)).toBe(true);
    expect(pointInLine(ORIGIN, 0, 5000, 1000, withinWidth)).toBe(true);
    expect(pointInLine(ORIGIN, 0, 5000, 1000, beyondWidth)).toBe(false);
  });

  it("point-in-line: beyond the length or behind the origin is excluded", () => {
    expect(pointInLine(ORIGIN, 0, 5000, 1000, { x: 5001, y: 0, z: 0 })).toBe(false);
    expect(pointInLine(ORIGIN, 0, 5000, 1000, { x: -100, y: 0, z: 0 })).toBe(false);
  });
});

describe("resolveTelegraphs", () => {
  it("hits actors standing in a due cone telegraph and spares the one behind it, ignoring the source", () => {
    const tg: Telegraph = {
      id: 1,
      sourceId: 99,
      abilityId: "boss_cleave",
      shape: "cone",
      origin: ORIGIN,
      yaw: 0,
      angle1024: 200,
      radius: 6000,
      resolvesTick: 10,
    };
    const state: SimState = {
      tick: 10,
      seed: 1,
      prng: [1, 2, 3, 4],
      actors: {
        2: playerActor(2, { x: 3000, y: 0, z: 0 }), // in front (yaw 0 = +x): hit
        3: playerActor(3, { x: -3000, y: 0, z: 0 }), // behind: not hit
        99: playerActor(99, ORIGIN), // the source itself, never hit
      },
      runTimerTicks: 10,
      phase: "combat",
      log: [],
    };
    const [result] = resolveTelegraphs(state, baseEncounter([tg]));
    expect(result?.telegraphId).toBe(1);
    expect(result?.hitActorIds).toEqual([2]);
  });

  it("does not resolve a telegraph before its resolvesTick", () => {
    const tg: Telegraph = {
      id: 1,
      sourceId: 99,
      abilityId: "boss_cleave",
      shape: "circle",
      origin: ORIGIN,
      yaw: 0,
      radius: 6000,
      resolvesTick: 10,
    };
    const state: SimState = {
      tick: 9,
      seed: 1,
      prng: [1, 2, 3, 4],
      actors: { 2: playerActor(2, ORIGIN) },
      runTimerTicks: 9,
      phase: "combat",
      log: [],
    };
    expect(resolveTelegraphs(state, baseEncounter([tg]))).toHaveLength(0);
  });
});
