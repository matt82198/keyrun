/**
 * tests/bots/windows.test.ts
 *
 * bots/src/shared/windows.ts: finds open enemy Windows and enforces design/BIBLE.md Sec 9 "The
 * Lock" -- a class can never close a Window its own class opened.
 *
 * Usage: `npx vitest run tests/bots/windows.test.ts`
 */
import { describe, expect, it } from "vitest";
import { openWindowsOn, canClose, findClosableWindow } from "@bots/src/shared/windows.js";
import { WINDOW_EFFECT_PREFIX } from "@bots/src/shared/pings.js";
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

describe("canClose (The Lock)", () => {
  it("any class can close a Window with an unknown source", () => {
    expect(canClose(null, "cinder")).toBe(true);
  });
  it("a class cannot close its own Window", () => {
    expect(canClose("warden", "warden")).toBe(false);
  });
  it("a different class can close it", () => {
    expect(canClose("warden", "cinder")).toBe(true);
  });
});

describe("openWindowsOn", () => {
  it("finds a live Window effect and derives setupClass from the caster's (sourceId's) classId", () => {
    const warden = makeActor({ id: 2, classId: "warden" });
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}sundered`, sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const wins = openWindowsOn(table([warden, enemy]), 10);
    expect(wins).toEqual([{ targetId: 100, windowId: "sundered", setupClass: "warden", endsTick: 200 }]);
  });

  it("ignores an expired Window (endsTick at or before the current tick)", () => {
    const warden = makeActor({ id: 2, classId: "warden" });
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}sundered`, sourceId: 2, stacks: 1, endsTick: 50, nextPeriodicTick: null }],
    });
    expect(openWindowsOn(table([warden, enemy]), 50)).toEqual([]);
  });

  it("ignores non-Window effects and dead enemies", () => {
    const warden = makeActor({ id: 2, classId: "warden" });
    const alive = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: "burning", sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const dead = makeActor({
      id: 101,
      kind: "enemy",
      alive: false,
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}kindled`, sourceId: 2, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    expect(openWindowsOn(table([warden, alive, dead]), 10)).toEqual([]);
  });

  it("setupClass is null when the source actor is missing (e.g. despawned)", () => {
    const enemy = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}sundered`, sourceId: 999, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    expect(openWindowsOn(table([enemy]), 10)[0]?.setupClass).toBeNull();
  });
});

describe("findClosableWindow", () => {
  it("excludes a Window this payoffClass opened, even if it's the only one, and picks the closable one otherwise", () => {
    const warden = makeActor({ id: 2, classId: "warden" });
    const cinder = makeActor({ id: 3, classId: "cinder" });
    const ownWindow = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}kindled`, sourceId: 3, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    const otherWindow = makeActor({
      id: 101,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}sundered`, sourceId: 2, stacks: 1, endsTick: 300, nextPeriodicTick: null }],
    });
    const actors = table([warden, cinder, ownWindow, otherWindow]);

    const forCinder = findClosableWindow(actors, 10, "cinder");
    expect(forCinder?.targetId).toBe(101);
    expect(forCinder?.setupClass).toBe("warden");
  });

  it("returns null when every open Window belongs to the querying class", () => {
    const cinder = makeActor({ id: 3, classId: "cinder" });
    const ownWindow = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}kindled`, sourceId: 3, stacks: 1, endsTick: 200, nextPeriodicTick: null }],
    });
    expect(findClosableWindow(table([cinder, ownWindow]), 10, "cinder")).toBeNull();
  });

  it("prefers the earliest-expiring closable Window", () => {
    const warden = makeActor({ id: 2, classId: "warden" });
    const soon = makeActor({
      id: 100,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}sundered`, sourceId: 2, stacks: 1, endsTick: 120, nextPeriodicTick: null }],
    });
    const later = makeActor({
      id: 101,
      kind: "enemy",
      effects: [{ effectId: `${WINDOW_EFFECT_PREFIX}sundered`, sourceId: 2, stacks: 1, endsTick: 300, nextPeriodicTick: null }],
    });
    const win = findClosableWindow(table([warden, soon, later]), 10, "cinder");
    expect(win?.targetId).toBe(100);
  });
});
