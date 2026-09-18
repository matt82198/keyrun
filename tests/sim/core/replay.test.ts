// Usage: vitest suite for sim/core/replay.ts record/apply and the golden-hash helper.
import { describe, expect, it } from "vitest";
import { record, apply, goldenHash } from "@sim/core/replay";
import type { Command } from "@sim/core/commands";
import type { StepRules } from "@sim/core/tick";

const spawnAndPoke: StepRules = (s, cmds, emit) => {
  if (s.tick === 0) {
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
      effects: [],
      threat: {},
    };
  }
  for (const c of cmds) {
    const actor = s.actors[c.actorId];
    if (actor && c.kind === "stopcast") actor.health -= 1;
    emit({ tick: s.tick, type: "command", kind: c.kind });
  }
};

describe("record", () => {
  it("returns a tick-sorted, independent copy of the command list", () => {
    const cmds: Command[] = [
      { tick: 2, actorId: 1, kind: "stopcast" },
      { tick: 0, actorId: 1, kind: "stopcast" },
    ];
    const rec = record(cmds);
    expect(rec.map((c) => c.tick)).toEqual([0, 2]);
    expect(rec).not.toBe(cmds);
    expect(rec[0]).not.toBe(cmds[1]);
    rec[0]!.actorId = 999;
    expect(cmds[1]!.actorId).toBe(1);
  });
});

describe("apply", () => {
  it("runs from tick 0 through the last command tick and applies effects", () => {
    const cmds: Command[] = [
      { tick: 0, actorId: 1, kind: "stopcast" },
      { tick: 2, actorId: 1, kind: "stopcast" },
    ];
    const { state, events } = apply(1, cmds, spawnAndPoke);
    expect(state.tick).toBe(3);
    expect(state.actors[1]!.health).toBe(98);
    expect(events).toHaveLength(2);
  });

  it("is deterministic: identical inputs replay to the identical final hash", () => {
    const cmds: Command[] = [
      { tick: 0, actorId: 1, kind: "stopcast" },
      { tick: 1, actorId: 1, kind: "stopcast" },
    ];
    const h1 = goldenHash(7, cmds, spawnAndPoke);
    const h2 = goldenHash(7, cmds, spawnAndPoke);
    expect(h1).toBe(h2);
  });

  it("handles an empty command list by staying at tick 0", () => {
    const { state } = apply(1, [], spawnAndPoke);
    expect(state.tick).toBe(0);
  });
});

describe("goldenHash", () => {
  it("changes when the command stream changes", () => {
    const rules = spawnAndPoke;
    const a = goldenHash(5, [{ tick: 0, actorId: 1, kind: "stopcast" }], rules);
    const b = goldenHash(5, [{ tick: 0, actorId: 1, kind: "target", targetId: null }], rules);
    expect(a).not.toBe(b);
  });

  it("equals hashState of the state returned by apply", async () => {
    const { hashState } = await import("@sim/core/state");
    const cmds: Command[] = [{ tick: 0, actorId: 1, kind: "stopcast" }];
    const h = goldenHash(2, cmds, spawnAndPoke);
    const { state } = apply(2, cmds, spawnAndPoke);
    expect(h).toBe(hashState(state));
  });
});
