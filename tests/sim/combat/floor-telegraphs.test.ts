/**
 * tests/sim/combat/floor-telegraphs.test.ts
 *
 * sim/combat/floor-telegraphs.ts: deriveFloorTelegraphs turns SimState.floorWindows into the
 * exact bots/src/shared/floor.ts id encoding (floor-patch-visibility backlog item) -- friendly
 * (player-sourced) floor Windows get "floor:window:<windowId>:<setupClass>", hostile
 * (enemy/boss-sourced, or orphaned) floor patches get "floor:<windowId>", every telegraph carries
 * danger:false, and an expired patch never appears.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import {
  deriveFloorTelegraphs,
  floorTelegraphId,
  floorWindowOpenedTelegraph,
  isFriendlyFloorWindow,
  FLOOR_PATCH_PREFIX,
} from "../../../sim/combat/floor-telegraphs";
import type { FloorWindow, SimState } from "../../../sim/combat/types";

function baseState(overrides: Partial<SimState> = {}): SimState {
  return {
    tick: 10,
    seed: 1,
    prng: [0, 0, 0, 0],
    actors: {},
    runTimerTicks: 0,
    phase: "combat",
    log: [],
    floorWindows: {},
    ...overrides,
  };
}

function floorWindow(overrides: Partial<FloorWindow> = {}): FloorWindow {
  return {
    windowId: "cinderfield",
    setupClass: "cinder",
    setupActorId: 1,
    pos: { x: 4500, y: 0, z: -1200 },
    endsTick: 170,
    radiusMm: 1800,
    ...overrides,
  };
}

describe("isFriendlyFloorWindow", () => {
  it("is friendly when the setup actor is a live player", () => {
    const cinder = createActor({ id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const state = baseState({ actors: { 1: cinder } });
    expect(isFriendlyFloorWindow(state, floorWindow({ setupActorId: 1 }))).toBe(true);
  });

  it("fails closed to hostile when the setup actor is an enemy (a boss's own floor Window)", () => {
    const boss = createActor({ id: 9, kind: "enemy", classId: "seed", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 5000, maxResource: 0 });
    const state = baseState({ actors: { 9: boss } });
    expect(isFriendlyFloorWindow(state, floorWindow({ setupActorId: 9, setupClass: "boss" }))).toBe(false);
  });

  it("fails closed to hostile when the setup actor can no longer be found", () => {
    const state = baseState({ actors: {} });
    expect(isFriendlyFloorWindow(state, floorWindow({ setupActorId: 404 }))).toBe(false);
  });
});

describe("floorTelegraphId", () => {
  it("nests window:<id>:<setupClass> after floor: for a friendly patch", () => {
    expect(floorTelegraphId(floorWindow({ windowId: "bulwark", setupClass: "warden" }), true)).toBe(
      `${FLOOR_PATCH_PREFIX}window:bulwark:warden`
    );
  });

  it("is bare floor:<windowId> for a hostile patch -- no nested window: segment", () => {
    expect(floorTelegraphId(floorWindow({ windowId: "seedbloom", setupClass: "boss" }), false)).toBe(
      `${FLOOR_PATCH_PREFIX}seedbloom`
    );
  });
});

describe("deriveFloorTelegraphs", () => {
  it("emits one danger:false circle Telegraph per open patch, with the right radius/expiry/id", () => {
    const cinder = createActor({ id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const fw = floorWindow();
    const state = baseState({ actors: { 1: cinder }, floorWindows: { [fw.windowId]: fw } });

    const telegraphs = deriveFloorTelegraphs(state);

    expect(telegraphs).toHaveLength(1);
    expect(telegraphs[0]).toEqual({
      id: "floor:window:cinderfield:cinder",
      shape: "circle",
      center: { x: 4500, z: -1200 },
      radius: 1800,
      resolvesTick: 170,
      danger: false,
    });
  });

  it("classifies a boss-sourced floor Window (seed.deathBloom-style) as hostile", () => {
    const boss = createActor({ id: 9, kind: "enemy", classId: "seed", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 5000, maxResource: 0 });
    const fw = floorWindow({ windowId: "seedbloom", setupClass: "boss", setupActorId: 9, pos: { x: 1000, y: 0, z: 2000 }, radiusMm: 2500, endsTick: 300 });
    const state = baseState({ tick: 250, actors: { 9: boss }, floorWindows: { [fw.windowId]: fw } });

    const telegraphs = deriveFloorTelegraphs(state);

    expect(telegraphs).toHaveLength(1);
    expect(telegraphs[0]?.id).toBe("floor:seedbloom");
    expect(telegraphs[0]?.danger).toBe(false);
    expect(telegraphs[0]?.center).toEqual({ x: 1000, z: 2000 });
    expect(telegraphs[0]?.radius).toBe(2500);
  });

  it("excludes a patch whose endsTick has already passed", () => {
    const cinder = createActor({ id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const fw = floorWindow({ endsTick: 5 });
    const state = baseState({ tick: 10, actors: { 1: cinder }, floorWindows: { [fw.windowId]: fw } });

    expect(deriveFloorTelegraphs(state)).toEqual([]);
  });

  it("returns [] when floorWindows is undefined (hand-built fixtures that predate the field)", () => {
    const state = baseState();
    delete state.floorWindows;
    expect(deriveFloorTelegraphs(state)).toEqual([]);
  });

  it("emits multiple friendly and hostile patches independently in the same tick", () => {
    const cinder = createActor({ id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const boss = createActor({ id: 9, kind: "enemy", classId: "seed", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 5000, maxResource: 0 });
    const friendly = floorWindow({ windowId: "bulwark", setupClass: "warden", setupActorId: 1 });
    const hostile = floorWindow({ windowId: "seedbloom", setupClass: "boss", setupActorId: 9 });
    const state = baseState({ actors: { 1: cinder, 9: boss }, floorWindows: { bulwark: friendly, seedbloom: hostile } });

    const ids = deriveFloorTelegraphs(state).map((t) => t.id).sort();
    expect(ids).toEqual(["floor:seedbloom", "floor:window:bulwark:warden"]);
  });
});

describe("floorWindowOpenedTelegraph", () => {
  it("matches deriveFloorTelegraphs' output for the same single patch", () => {
    const cinder = createActor({ id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
    const fw = floorWindow();
    const state = baseState({ actors: { 1: cinder }, floorWindows: { [fw.windowId]: fw } });

    expect(floorWindowOpenedTelegraph(state, fw)).toEqual(deriveFloorTelegraphs(state)[0]);
  });
});
