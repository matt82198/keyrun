/**
 * tests/sim/encounter/chase-tuning.test.ts
 *
 * Unit guards for per-enemy chase tuning (defects (d) speed and (e) margin), each layer on its own:
 * content/sim-adapt.ts's toSimDungeon copies EnemyDef.speedMmPerTick/chaseMarginMm onto the spawn,
 * sim/encounter/pulls.ts's spawnPull copies them onto ActorState (and omits them when absent, so
 * old content hashes unchanged), sim/combat/rules.ts walks a `move` at the actor's speed (base when
 * absent), and sim/encounter/chase.ts stops at `engage - (actor.chaseMarginMm ?? CHASE_MARGIN_MM)`.
 *
 * Usage: `npx vitest run tests/sim/encounter/chase-tuning.test.ts`
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { ActorState, SimState } from "../../../sim/encounter/types";
import { CHASE_MARGIN_MM, chaseCommands } from "../../../sim/encounter/chase";
import { spawnPull, type DungeonDef } from "../../../sim/encounter/pulls";
import { toSimDungeon } from "../../../content/sim-adapt";
import type { EnemyDef } from "../../../content/schema/enemy";
import type { DungeonDef as ContentDungeonDef } from "../../../content/schema/dungeon";

const SWING: AbilityDef = {
  id: "swing",
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

function stateWith(actors: ActorState[]): SimState {
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

function cfg(): RulesConfig {
  return { abilities: {}, effects: {}, lockoutTicks: 60, baseSpeedMmPerTick: 180, actorRadiusMm: 1, bounds: { minX: -99999, maxX: 99999, minZ: -99999, maxZ: 99999 } };
}

function walker(speed?: number): ActorState {
  const a = createActor({ id: 7, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
  return speed === undefined ? a : { ...a, speedMmPerTick: speed };
}

function walkOneTick(actor: ActorState): number {
  const state = stateWith([actor]);
  makeRules(cfg())(state, [{ tick: 0, actorId: 7, kind: "move", dir: { x: 0, z: 1000 }, yaw: 0 }], () => {});
  return state.actors[7]!.pos.z;
}

describe("rules.ts: move walks at the actor's speed", () => {
  it("uses ActorState.speedMmPerTick when set", () => {
    expect(walkOneTick(walker(207))).toBe(207);
  });
  it("falls back to RulesConfig.baseSpeedMmPerTick when absent", () => {
    expect(walkOneTick(walker())).toBe(180);
  });
});

describe("chase.ts: per-enemy margin", () => {
  function chaseAt(distance: number, margin?: number): number {
    const enemy = { ...walker(), ...(margin !== undefined ? { chaseMarginMm: margin } : {}) };
    const player = createActor({ id: 1, kind: "player", classId: "w", role: "tank", pos: { x: 0, y: 0, z: distance }, maxHealth: 1, maxResource: 0 });
    return chaseCommands(stateWith([enemy, player]), { 7: [{ at: "loop", every: 40, cast: "swing" }] }, { swing: SWING }).length;
  }
  it("defaults to CHASE_MARGIN_MM", () => {
    expect(chaseAt(3000 - CHASE_MARGIN_MM)).toBe(0);
    expect(chaseAt(3000 - CHASE_MARGIN_MM + 1)).toBe(1);
  });
  it("honours ActorState.chaseMarginMm", () => {
    expect(chaseAt(2900, 100)).toBe(0);
    expect(chaseAt(2901, 100)).toBe(1);
    expect(chaseAt(2600)).toBe(1); // the same gap still chases under the 500 default
  });
});

describe("content -> spawn -> actor: tuning is copied only when present", () => {
  const enemies = new Map<string, EnemyDef>([
    ["fast", { id: "fast", name: "F", maxHealth: 10, role: "melee", abilities: [], script: [], speedMmPerTick: 207, chaseMarginMm: 300 }],
    ["plain", { id: "plain", name: "P", maxHealth: 10, role: "melee", abilities: [], script: [] }],
  ]);
  const dungeon = {
    id: "d",
    name: "D",
    parTicks: 1,
    pulls: [{ id: "p", name: "P", teaches: "t", enemies: [{ enemyId: "fast", count: 1, spawn: { x: 0, z: 0 } }, { enemyId: "plain", count: 1, spawn: { x: 0, z: 0 } }] }],
    boss: { enemyId: "fast", mechanics: [] },
  } as unknown as ContentDungeonDef;

  it("toSimDungeon + spawnPull carry speed/margin onto the fast enemy and nothing onto the plain one", () => {
    const sim: DungeonDef = toSimDungeon(dungeon, enemies, 1000).dungeon;
    const [fast, plain] = spawnPull(sim, 0, 0, () => {});
    expect(fast!.speedMmPerTick).toBe(207);
    expect(fast!.chaseMarginMm).toBe(300);
    expect("speedMmPerTick" in plain!).toBe(false);
    expect("chaseMarginMm" in plain!).toBe(false);
  });
});
