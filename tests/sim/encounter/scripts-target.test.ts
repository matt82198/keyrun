/**
 * tests/sim/encounter/scripts-target.test.ts — unit tests for sim/encounter/script-target.ts's
 * resolveScriptTarget: one test per targeting rule (see that file's docstring for the full spec).
 */
import { describe, expect, it } from "vitest";
import { resolveScriptTarget } from "../../../sim/encounter/script-target";
import type { AbilityDef } from "../../../sim/combat/abilities";
import type { ActorState, SimState } from "../../../sim/encounter/types";

function makeActor(overrides: Partial<ActorState> & { id: number; kind: "player" | "enemy" }): ActorState {
  return {
    classId: overrides.kind === "enemy" ? "boss" : "warden",
    role: overrides.kind === "enemy" ? "none" : "tank",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 1000,
    maxHealth: 1000,
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

function makeState(actors: ActorState[], tick = 100): SimState {
  const byId: SimState["actors"] = {};
  for (const a of actors) byId[a.id] = a;
  return { tick, seed: 1, prng: [111, 222, 333, 444], actors: byId, runTimerTicks: tick, phase: "combat", log: [] };
}

const TARGET_ABILITY: AbilityDef = {
  id: "hostile.hit",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "none", amount: 0 },
  range: 3000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "damage", amount: 10, school: "physical" }],
};

const HEAL_ABILITY: AbilityDef = { ...TARGET_ABILITY, id: "ally.heal", effects: [{ op: "heal", amount: 10 }] };
const SELF_ABILITY: AbilityDef = { ...TARGET_ABILITY, id: "self.buff", range: 0, shape: { kind: "self" } };
const CIRCLE_ABILITY: AbilityDef = { ...TARGET_ABILITY, id: "aoe.circle", range: 0, shape: { kind: "circle", radius: 2000 } };

describe("resolveScriptTarget", () => {
  it("hostile default: picks the caster's highest-threat living player", () => {
    const caster = makeActor({ id: 1, kind: "enemy", threat: { 10: 50, 11: 200 } });
    const p10 = makeActor({ id: 10, kind: "player" });
    const p11 = makeActor({ id: 11, kind: "player" });
    const state = makeState([caster, p10, p11]);
    const result = resolveScriptTarget({ state, caster, ability: TARGET_ABILITY });
    expect(result).toEqual({ targetId: 11, blocked: false });
  });

  it("hostile fallback: no threat entries falls back to the nearest living player", () => {
    const caster = makeActor({ id: 1, kind: "enemy", pos: { x: 0, y: 0, z: 0 } });
    const far = makeActor({ id: 10, kind: "player", pos: { x: 0, y: 0, z: 2000 } });
    const near = makeActor({ id: 11, kind: "player", pos: { x: 0, y: 0, z: 500 } });
    const state = makeState([caster, far, near]);
    const result = resolveScriptTarget({ state, caster, ability: TARGET_ABILITY });
    expect(result).toEqual({ targetId: 11, blocked: false });
  });

  it("hostile: a dead top-threat entry is skipped in favour of the nearest living player", () => {
    const caster = makeActor({ id: 1, kind: "enemy", threat: { 10: 999 } });
    const dead = makeActor({ id: 10, kind: "player", alive: false });
    const living = makeActor({ id: 11, kind: "player", pos: { x: 0, y: 0, z: 100 } });
    const state = makeState([caster, dead, living]);
    const result = resolveScriptTarget({ state, caster, ability: TARGET_ABILITY });
    expect(result).toEqual({ targetId: 11, blocked: false });
  });

  it("heal ability: targets the lowest-health living ally enemy over any player", () => {
    const caster = makeActor({ id: 1, kind: "enemy" });
    const player = makeActor({ id: 10, kind: "player" });
    const allyHigh = makeActor({ id: 20, kind: "enemy", health: 900 });
    const allyLow = makeActor({ id: 21, kind: "enemy", health: 100 });
    const state = makeState([caster, player, allyHigh, allyLow]);
    const result = resolveScriptTarget({ state, caster, ability: HEAL_ABILITY });
    expect(result).toEqual({ targetId: 21, blocked: false });
  });

  it("heal ability: falls back to self when no other living ally exists", () => {
    const caster = makeActor({ id: 1, kind: "enemy" });
    const player = makeActor({ id: 10, kind: "player" });
    const state = makeState([caster, player]);
    const result = resolveScriptTarget({ state, caster, ability: HEAL_ABILITY });
    expect(result).toEqual({ targetId: 1, blocked: false });
  });

  it("self-shaped ability always targets itself, bypassing threat/range entirely", () => {
    const caster = makeActor({ id: 1, kind: "enemy", pos: { x: 5000, y: 0, z: 5000 } });
    const player = makeActor({ id: 10, kind: "player", pos: { x: 0, y: 0, z: 0 } });
    const state = makeState([caster, player]);
    const result = resolveScriptTarget({ state, caster, ability: SELF_ABILITY });
    expect(result).toEqual({ targetId: 1, blocked: false });
  });

  it("range gating: a player outside ability.range is excluded from candidates", () => {
    const caster = makeActor({ id: 1, kind: "enemy", pos: { x: 0, y: 0, z: 0 } });
    const outOfRange = makeActor({ id: 10, kind: "player", pos: { x: 0, y: 0, z: 5000 } });
    const inRange = makeActor({ id: 11, kind: "player", pos: { x: 0, y: 0, z: 1000 } });
    const state = makeState([caster, outOfRange, inRange]);
    const result = resolveScriptTarget({ state, caster, ability: TARGET_ABILITY }); // range 3000
    expect(result).toEqual({ targetId: 11, blocked: false });
  });

  it("range gating: nothing in range on a target-shaped ability blocks the cast (no spam)", () => {
    const caster = makeActor({ id: 1, kind: "enemy", pos: { x: 0, y: 0, z: 0 } });
    const outOfRange = makeActor({ id: 10, kind: "player", pos: { x: 0, y: 0, z: 5000 } });
    const state = makeState([caster, outOfRange]);
    const result = resolveScriptTarget({ state, caster, ability: TARGET_ABILITY });
    expect(result).toEqual({ targetId: undefined, blocked: true });
  });

  it("a known non-target, non-self shape (circle/cone/line) needs no target and is left alone", () => {
    const caster = makeActor({ id: 1, kind: "enemy" });
    const player = makeActor({ id: 10, kind: "player" });
    const state = makeState([caster, player]);
    const result = resolveScriptTarget({ state, caster, ability: CIRCLE_ABILITY });
    expect(result).toEqual({ targetId: undefined, blocked: false });
  });

  it('override "self" forces itself even on a hostile target-shaped ability', () => {
    const caster = makeActor({ id: 1, kind: "enemy" });
    const player = makeActor({ id: 10, kind: "player" });
    const state = makeState([caster, player]);
    const result = resolveScriptTarget({ state, caster, ability: TARGET_ABILITY, override: "self" });
    expect(result).toEqual({ targetId: 1, blocked: false });
  });

  it('override "threat" forces the player pool + threat/nearest selection even on a heal ability', () => {
    const caster = makeActor({ id: 1, kind: "enemy", threat: { 10: 5 } });
    const player = makeActor({ id: 10, kind: "player" });
    const ally = makeActor({ id: 20, kind: "enemy", health: 1 });
    const state = makeState([caster, player, ally]);
    const result = resolveScriptTarget({ state, caster, ability: HEAL_ABILITY, override: "threat" });
    expect(result).toEqual({ targetId: 10, blocked: false });
  });

  it('override "random" deterministically picks one in-range candidate from the same state', () => {
    const caster = makeActor({ id: 1, kind: "enemy" });
    const p1 = makeActor({ id: 10, kind: "player" });
    const p2 = makeActor({ id: 11, kind: "player" });
    const state = makeState([caster, p1, p2]);
    const a = resolveScriptTarget({ state, caster, ability: TARGET_ABILITY, override: "random" });
    const b = resolveScriptTarget({ state, caster, ability: TARGET_ABILITY, override: "random" });
    expect(a).toEqual(b);
    expect([10, 11]).toContain(a.targetId);
    expect(a.blocked).toBe(false);
  });

  it("ability unknown (the real, unwired call path): resolves the hostile default whenever a living player exists", () => {
    const caster = makeActor({ id: 1, kind: "enemy", pos: { x: 0, y: 0, z: 0 } });
    const player = makeActor({ id: 10, kind: "player", pos: { x: 0, y: 0, z: 100 } });
    const state = makeState([caster, player]);
    const result = resolveScriptTarget({ state, caster });
    expect(result).toEqual({ targetId: 10, blocked: false });
  });

  it("ability unknown and no living player: never blocks (preserves the pre-fix cast-anyway behaviour)", () => {
    const caster = makeActor({ id: 1, kind: "enemy" });
    const state = makeState([caster]);
    const result = resolveScriptTarget({ state, caster });
    expect(result).toEqual({ targetId: undefined, blocked: false });
  });
});
