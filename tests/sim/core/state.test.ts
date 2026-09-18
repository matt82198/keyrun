// Usage: vitest suite for sim/core/state.ts SimState shape, clone, and hash.
import { describe, expect, it } from "vitest";
import { createState, cloneState, hashState } from "@sim/core/state";

describe("createState", () => {
  it("builds the initial shape", () => {
    const s = createState(123);
    expect(s.tick).toBe(0);
    expect(s.seed).toBe(123);
    expect(s.prng).toHaveLength(4);
    expect(s.actors).toEqual({});
    expect(s.runTimerTicks).toBe(0);
    expect(typeof s.phase).toBe("string");
    expect(s.log).toEqual([]);
  });

  it("is deterministic for the same seed", () => {
    expect(createState(5)).toEqual(createState(5));
  });
});

describe("cloneState", () => {
  it("produces a deep, independent copy", () => {
    const s = createState(1);
    s.actors[1] = {
      id: 1,
      kind: "player",
      classId: "warrior",
      role: "tank",
      pos: { x: 0, y: 0, z: 0 },
      yaw: 0,
      health: 100,
      maxHealth: 100,
      resource: 0,
      maxResource: 100,
      alive: true,
      targetId: null,
      castId: null,
      castEndsTick: null,
      castStartTick: null,
      cooldowns: {},
      gcdReadyTick: 0,
      effects: [{ effectId: "buff", sourceId: 1, stacks: 1, endsTick: 10, nextPeriodicTick: null }],
      threat: {},
    };
    const clone = cloneState(s);
    expect(clone).toEqual(s);
    expect(clone).not.toBe(s);
    expect(clone.actors[1]).not.toBe(s.actors[1]);

    clone.actors[1]!.health = 1;
    clone.actors[1]!.effects.push({ effectId: "dot", sourceId: 2, stacks: 1, endsTick: 5, nextPeriodicTick: 1 });
    expect(s.actors[1]!.health).toBe(100);
    expect(s.actors[1]!.effects).toHaveLength(1);
  });
});

describe("hashState", () => {
  it("is stable for equal states and changes when state changes", () => {
    const a = createState(1);
    const b = createState(1);
    expect(hashState(a)).toBe(hashState(b));

    const c = cloneState(a);
    c.tick = 1;
    expect(hashState(c)).not.toBe(hashState(a));
  });

  it("is independent of object key insertion order (canonical JSON)", () => {
    const a = createState(9);
    a.actors[1] = makeActor(1);
    a.actors[2] = makeActor(2);

    const b = createState(9);
    b.actors[2] = makeActor(2);
    b.actors[1] = makeActor(1);

    expect(hashState(a)).toBe(hashState(b));
  });

  it("matches after a clone round-trip", () => {
    const a = createState(3);
    a.actors[1] = makeActor(1);
    expect(hashState(cloneState(a))).toBe(hashState(a));
  });
});

function makeActor(id: number) {
  return {
    id,
    kind: "enemy" as const,
    classId: "grunt",
    role: "none" as const,
    pos: { x: id, y: 0, z: 0 },
    yaw: 0,
    health: 50,
    maxHealth: 50,
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
