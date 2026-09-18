// Usage: vitest suite for sim/core/tick.ts step() determinism and command ordering.
import { describe, expect, it } from "vitest";
import { step, TICK_HZ, TICK_MS } from "@sim/core/tick";
import { createState, hashState } from "@sim/core/state";
import type { Command } from "@sim/core/commands";
import type { StepRules } from "@sim/core/tick";

describe("tick constants", () => {
  it("derives TICK_MS from TICK_HZ as the single source of truth", () => {
    expect(TICK_HZ).toBe(20);
    expect(TICK_MS).toBe(Math.round(1000 / TICK_HZ));
    expect(TICK_MS).toBe(50);
  });
});

describe("step", () => {
  const noop: StepRules = () => {};

  it("advances tick and runTimerTicks by one and does not mutate the input", () => {
    const s0 = createState(1);
    const before = hashState(s0);
    const { state } = step(s0, [], noop);
    expect(hashState(s0)).toBe(before);
    expect(state.tick).toBe(1);
    expect(state.runTimerTicks).toBe(1);
    expect(state).not.toBe(s0);
  });

  it("drops commands whose tick does not match the current tick", () => {
    const s0 = createState(1);
    const seen: Command[] = [];
    const rules: StepRules = (_s, cmds) => seen.push(...cmds);
    step(s0, [{ tick: 1, actorId: 1, kind: "stopcast" }], rules);
    expect(seen).toHaveLength(0);
  });

  it("drops malformed commands instead of throwing", () => {
    const s0 = createState(1);
    const seen: Command[] = [];
    const rules: StepRules = (_s, cmds) => seen.push(...cmds);
    const bad = { tick: 0, actorId: 1, kind: "explode" } as unknown as Command;
    expect(() => step(s0, [bad], rules)).not.toThrow();
    step(s0, [bad], rules);
    expect(seen).toHaveLength(0);
  });

  it("sorts commands by (actorId, kind, insertion order) before handing them to rules", () => {
    const s0 = createState(1);
    const seen: string[] = [];
    const rules: StepRules = (_s, cmds) => {
      for (const c of cmds) seen.push(`${c.actorId}:${c.kind}`);
    };
    const cmds: Command[] = [
      { tick: 0, actorId: 2, kind: "move", dir: { x: 0, z: 0 }, yaw: 0 },
      { tick: 0, actorId: 1, kind: "stopcast" },
      { tick: 0, actorId: 1, kind: "cast", abilityId: "a" },
      { tick: 0, actorId: 1, kind: "target", targetId: null },
      { tick: 0, actorId: 2, kind: "cast", abilityId: "b" },
    ];
    step(s0, cmds, rules);
    expect(seen).toEqual(["1:cast", "1:stopcast", "1:target", "2:cast", "2:move"]);
  });

  it("is deterministic across repeated runs and clones", () => {
    const rules: StepRules = (s, cmds, emit) => {
      for (const c of cmds) {
        if (c.kind === "stopcast") s.runTimerTicks += 1;
        emit({ tick: s.tick, type: "seen", actorId: c.actorId });
      }
    };
    const cmds: Command[] = [{ tick: 0, actorId: 1, kind: "stopcast" }];
    const hashes = [0, 1, 2].map(() => hashState(step(createState(42), cmds, rules).state));
    expect(hashes[0]).toBe(hashes[1]);
    expect(hashes[1]).toBe(hashes[2]);
  });
});
