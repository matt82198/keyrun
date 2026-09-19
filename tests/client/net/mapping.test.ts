// Usage: pure-function coverage for client/src/net/mapping.ts -- class/role resolution, the
// server-snapshot -> client-Snapshot translation (the "identical to the local host" contract), the
// content-agnostic enemy-target cycle, and host-command -> wire-command translation.
import { describe, expect, it } from "vitest";
import { cycleEnemyTarget, resolveClassId, roleForClassId, toClientSnapshot, translateHostCommand } from "../../../client/src/net/mapping.js";
import type { ServerActorSnapshot, ServerSnapshot } from "../../../client/src/net/wire-types.js";

function actor(overrides: Partial<ServerActorSnapshot> = {}): ServerActorSnapshot {
  return {
    id: 1,
    kind: "player",
    role: "tank",
    classId: "warden",
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
    controlledBy: "human",
    ...overrides,
  };
}

function snapshot(overrides: Partial<ServerSnapshot> = {}): ServerSnapshot {
  return {
    tick: 10,
    actors: [actor()],
    telegraphs: [],
    runTimerTicks: 10,
    timerLimitTicks: 1000,
    slackTicks: 5,
    maxSlackTicks: 5,
    depleted: false,
    parTicks: 1000,
    pullIndex: 0,
    pullCount: 2,
    stateHash: "deadbeef",
    ...overrides,
  };
}

describe("mapping: class/role resolution", () => {
  it("resolves the three real classes case-insensitively, defaulting to warden", () => {
    expect(resolveClassId("Cinder")).toBe("cinder");
    expect(resolveClassId("chanter")).toBe("chanter");
    expect(resolveClassId("paladin")).toBe("warden");
    expect(resolveClassId(null)).toBe("warden");
  });

  it("mirrors server/src/roster.ts's ROLE_ROSTER class -> role binding", () => {
    expect(roleForClassId("warden")).toBe("tank");
    expect(roleForClassId("chanter")).toBe("healer");
    expect(roleForClassId("cinder")).toBe("damage");
  });
});

describe("mapping: toClientSnapshot", () => {
  it("strips server-only fields (stateHash, per-actor controlledBy) and keeps everything else", () => {
    const s = snapshot();
    const out = toClientSnapshot(s);
    expect(out).toEqual({
      tick: 10,
      actors: [{ id: 1, kind: "player", role: "tank", classId: "warden", pos: { x: 0, y: 0, z: 0 }, yaw: 0, health: 100, maxHealth: 100, resource: 0, maxResource: 100, alive: true, targetId: null, cast: null, cooldowns: {} }],
      telegraphs: [],
      runTimerTicks: 10,
      timerLimitTicks: 1000,
      slackTicks: 5,
      maxSlackTicks: 5,
      depleted: false,
      parTicks: 1000,
      pullIndex: 0,
      pullCount: 2,
      runEnd: null,
    });
    expect(out).not.toHaveProperty("stateHash");
  });

  it("marks the run ended (abandoned) when every player actor is dead", () => {
    const s = snapshot({ actors: [actor({ id: 1, alive: false }), actor({ id: 2, alive: false })] });
    expect(toClientSnapshot(s).runEnd).toEqual({ outcome: "abandoned", tick: 10 });
  });

  it("marks the run ended (timed/depleted) when the final pull's enemies are all dead", () => {
    const enemy = actor({ id: 900, kind: "enemy", alive: false });
    const timed = snapshot({ pullIndex: 1, pullCount: 2, depleted: false, actors: [actor(), enemy] });
    expect(toClientSnapshot(timed).runEnd).toEqual({ outcome: "timed", tick: 10 });

    const depleted = snapshot({ pullIndex: 1, pullCount: 2, depleted: true, actors: [actor(), enemy] });
    expect(toClientSnapshot(depleted).runEnd).toEqual({ outcome: "depleted", tick: 10 });
  });

  it("is not ended mid-run (players alive, non-final pull, or enemies still up)", () => {
    const s = snapshot({ pullIndex: 0, pullCount: 2, actors: [actor(), actor({ id: 900, kind: "enemy", alive: true })] });
    expect(toClientSnapshot(s).runEnd).toBeNull();
  });
});

describe("mapping: cycleEnemyTarget", () => {
  const enemies = [
    { id: 3, alive: true },
    { id: 1, alive: false },
    { id: 2, alive: true },
  ];

  it("picks the first alive enemy by ascending id when nothing is targeted", () => {
    expect(cycleEnemyTarget(enemies, null)).toBe(2);
  });

  it("cycles to the next alive id, wrapping around", () => {
    expect(cycleEnemyTarget(enemies, 2)).toBe(3);
    expect(cycleEnemyTarget(enemies, 3)).toBe(2);
  });

  it("returns null when nothing is alive", () => {
    expect(cycleEnemyTarget([{ id: 1, alive: false }], null)).toBeNull();
  });

  it("restarts from the first alive id when the current target is no longer in the list", () => {
    expect(cycleEnemyTarget(enemies, 999)).toBe(2);
  });
});

describe("mapping: translateHostCommand", () => {
  const ctx = { selfId: 1, targetId: 5, enemies: [{ id: 5, alive: true }, { id: 6, alive: true }] };

  it("move -> sim/core/commands.ts's move shape, yaw derived from (dx, dz)", () => {
    const out = translateHostCommand({ type: "move", dx: 180, dz: 0, tick: 3 }, ctx);
    expect(out).toEqual({ tick: 3, kind: "move", dir: { x: 180, z: 0 }, yaw: 0 });
  });

  it("cast -> targets the current target, or self.selfId when nothing is targeted", () => {
    expect(translateHostCommand({ type: "cast", abilityId: "smite", tick: 4 }, ctx)).toEqual({ tick: 4, kind: "cast", abilityId: "smite", targetId: 5 });
    expect(translateHostCommand({ type: "cast", abilityId: "heal", tick: 4 }, { ...ctx, targetId: null })).toEqual({ tick: 4, kind: "cast", abilityId: "heal", targetId: 1 });
  });

  it("targetCycle -> a target command with the next alive enemy id", () => {
    expect(translateHostCommand({ type: "targetCycle", tick: 6 }, ctx)).toEqual({ tick: 6, kind: "target", targetId: 6 });
  });

  it("stopcast -> a bare stopcast command", () => {
    expect(translateHostCommand({ type: "stopcast", tick: 7 }, ctx)).toEqual({ tick: 7, kind: "stopcast" });
  });

  it("has no wire equivalent for host controls with no sim meaning (abandon)", () => {
    expect(translateHostCommand({ type: "abandon", tick: 8 }, ctx)).toBeNull();
  });
});
