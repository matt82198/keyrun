/**
 * tests/bots/ledger.test.ts
 *
 * bots/src/shared/ledger.ts: the kick ledger (bots/CLAUDE.md: "the bot with the lowest actorId
 * whose kick is ready takes it"; design/BIBLE.md Sec 9 "The Choir" -- a must-kick cast outranks
 * any other simultaneous cast via a priority-cast list).
 *
 * Usage: `npx vitest run tests/bots/ledger.test.ts`
 */
import { describe, expect, it } from "vitest";
import { selectPriorityCastTarget, selectKicker, resolveInterrupt, type CastingTarget } from "@bots/src/shared/ledger.js";
import type { ActorId, ActorState } from "@bots/src/types.js";

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1,
    kind: "player",
    classId: "warden",
    role: "damage",
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

function table(actors: ActorState[]): Record<ActorId, ActorState> {
  const out: Record<ActorId, ActorState> = {};
  for (const a of actors) out[a.id] = a;
  return out;
}

describe("selectPriorityCastTarget", () => {
  it("with no priority list, ties on lowest actorId", () => {
    const casting: CastingTarget[] = [
      { actorId: 101, abilityId: "a" },
      { actorId: 100, abilityId: "b" },
    ];
    expect(selectPriorityCastTarget(casting)).toBe(100);
  });

  it("a listed ability outranks an unlisted one regardless of actorId", () => {
    const casting: CastingTarget[] = [
      { actorId: 100, abilityId: "trivial-swing" },
      { actorId: 200, abilityId: "must-kick-heal" },
    ];
    expect(selectPriorityCastTarget(casting, ["must-kick-heal"])).toBe(200);
  });

  it("null when nothing is casting", () => {
    expect(selectPriorityCastTarget([])).toBeNull();
  });
});

describe("selectKicker", () => {
  it("the lowest of the eligible actorIds", () => {
    expect(selectKicker([7, 2, 5])).toBe(2);
  });
  it("null when no one is eligible", () => {
    expect(selectKicker([])).toBeNull();
  });
});

describe("resolveInterrupt", () => {
  it("with two ready interrupts, the lowest actorId is assigned the kick", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const low = makeActor({ id: 3 });
    const high = makeActor({ id: 7 });
    const assignment = resolveInterrupt(table([enemy, low, high]), "kick", [3, 7], 10);
    expect(assignment).toEqual({ targetId: 100, kickerId: 3 });
  });

  it("skips a not-ready candidate: the other eligible actor is assigned instead", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const low = makeActor({ id: 3, cooldowns: { kick: 999 } });
    const high = makeActor({ id: 7 });
    const assignment = resolveInterrupt(table([enemy, low, high]), "kick", [3, 7], 10);
    expect(assignment).toEqual({ targetId: 100, kickerId: 7 });
  });

  it("null when nobody eligible is ready", () => {
    const enemy = makeActor({ id: 100, kind: "enemy", castId: "big-cast" });
    const low = makeActor({ id: 3, cooldowns: { kick: 999 } });
    expect(resolveInterrupt(table([enemy, low]), "kick", [3], 10)).toBeNull();
  });

  it("null when nothing is casting even if kickers are ready", () => {
    const low = makeActor({ id: 3 });
    expect(resolveInterrupt(table([low]), "kick", [3], 10)).toBeNull();
  });

  it("honours a priority-cast list: the must-kick cast is targeted even from a higher-actorId enemy", () => {
    const trivial = makeActor({ id: 100, kind: "enemy", castId: "trivial-swing" });
    const mustKick = makeActor({ id: 200, kind: "enemy", castId: "choir.healCast" });
    const low = makeActor({ id: 3 });
    const assignment = resolveInterrupt(table([trivial, mustKick, low]), "kick", [3], 10, ["choir.healCast"]);
    expect(assignment).toEqual({ targetId: 200, kickerId: 3 });
  });
});
