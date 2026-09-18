import { describe, expect, it } from "vitest";
import { interpolateSnapshots } from "../../client/src/sim-host/interpolate.js";
import type { ActorSnapshot, Snapshot } from "../../client/src/sim-host/snapshot.js";

function actor(overrides: Partial<ActorSnapshot> & { id: number }): ActorSnapshot {
  return {
    kind: "player",
    role: "damage",
    classId: "test",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 100,
    maxHealth: 100,
    resource: 0,
    maxResource: 100,
    alive: true,
    targetId: null,
    cast: null,
    cooldowns: {},
    ...overrides,
  };
}

function snapshot(tick: number, actors: ActorSnapshot[]): Snapshot {
  return { tick, actors, telegraphs: [], runTimerTicks: tick, timerLimitTicks: 1000 };
}

describe("interpolateSnapshots", () => {
  it("returns prev's transform at alpha 0", () => {
    const prev = snapshot(0, [actor({ id: 1, pos: { x: 0, y: 0, z: 0 } })]);
    const next = snapshot(1, [actor({ id: 1, pos: { x: 1000, y: 0, z: 0 } })]);
    const result = interpolateSnapshots(prev, next, 0);
    expect(result[0]!.pos).toEqual({ x: 0, y: 0, z: 0 });
  });

  it("returns next's transform at alpha 1", () => {
    const prev = snapshot(0, [actor({ id: 1, pos: { x: 0, y: 0, z: 0 } })]);
    const next = snapshot(1, [actor({ id: 1, pos: { x: 1000, y: 0, z: 0 } })]);
    const result = interpolateSnapshots(prev, next, 1);
    expect(result[0]!.pos).toEqual({ x: 1000, y: 0, z: 0 });
  });

  it("linearly interpolates position at alpha 0.5", () => {
    const prev = snapshot(0, [actor({ id: 1, pos: { x: 0, y: 0, z: 100 } })]);
    const next = snapshot(1, [actor({ id: 1, pos: { x: 1000, y: 200, z: 300 } })]);
    const result = interpolateSnapshots(prev, next, 0.5);
    expect(result[0]!.pos).toEqual({ x: 500, y: 100, z: 200 });
  });

  it("clamps alpha outside [0, 1]", () => {
    const prev = snapshot(0, [actor({ id: 1, pos: { x: 0, y: 0, z: 0 } })]);
    const next = snapshot(1, [actor({ id: 1, pos: { x: 1000, y: 0, z: 0 } })]);
    expect(interpolateSnapshots(prev, next, -5)[0]!.pos).toEqual({ x: 0, y: 0, z: 0 });
    expect(interpolateSnapshots(prev, next, 5)[0]!.pos).toEqual({ x: 1000, y: 0, z: 0 });
  });

  it("interpolates yaw along the shortest path across the 0/1023 wrap", () => {
    const prev = snapshot(0, [actor({ id: 1, yaw: 1000 })]);
    const next = snapshot(1, [actor({ id: 1, yaw: 20 })]);
    const result = interpolateSnapshots(prev, next, 0.5);
    expect(result[0]!.yaw).toBe(1022);
  });

  it("snaps newly spawned actors (absent in prev) straight to next's transform", () => {
    const prev = snapshot(0, []);
    const next = snapshot(1, [actor({ id: 2, pos: { x: 500, y: 0, z: 500 }, yaw: 300 })]);
    const result = interpolateSnapshots(prev, next, 0.5);
    expect(result[0]!.pos).toEqual({ x: 500, y: 0, z: 500 });
    expect(result[0]!.yaw).toBe(300);
  });

  it("takes discrete fields (health, cast, cooldown-driven state) from next as-is", () => {
    const prev = snapshot(0, [actor({ id: 1, health: 100 })]);
    const next = snapshot(1, [actor({ id: 1, health: 40, cast: { abilityId: "1", startTick: 1, endTick: 5 } })]);
    const result = interpolateSnapshots(prev, next, 0.5);
    expect(result[0]!.health).toBe(40);
    expect(result[0]!.cast).toEqual({ abilityId: "1", startTick: 1, endTick: 5 });
  });
});
