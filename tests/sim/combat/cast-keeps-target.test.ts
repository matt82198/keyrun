/**
 * CAST-KEEPS-START-TARGET (finding f-bd8fbbb1, the WoW rule for the core loop): a cast resolves on the target it
 * started on. A "target" command that arrives mid-cast changes the caster's target for the NEXT press only; the cast
 * in flight still lands its effects and its Window on the target it began on. Driven through makeRules (cast start,
 * target command, completion at castEndsTick, then Windows). Twin of
 * game/sim/Keyrun.Sim.Tests/CastKeepsStartTargetTests.cs (D28: the C# sim is the oracle; this frozen tree records it).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";

const CASTER = 1;
const ALLY = 2;
const ENEMY_A = 10;
const ENEMY_B = 20;

const bolt: AbilityDef = {
  id: "bolt",
  castTicks: 20,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [
    { op: "damage", amount: 30, school: "arcane" },
    { op: "window", windowId: "attuned", durationTicks: 60, onTarget: true, setupClass: "chanter" },
  ],
};

const mend: AbilityDef = {
  id: "mend",
  castTicks: 20,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "heal", amount: 40 }],
};

function cfg(): RulesConfig {
  return {
    abilities: { bolt, mend },
    effects: {},
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
  };
}

function scenario(): SimState {
  const actors = [
    createActor({ id: CASTER, kind: "player", classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 0 }),
    createActor({ id: ALLY, kind: "player", classId: "warden", role: "tank", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 200, maxResource: 0 }),
    createActor({ id: ENEMY_A, kind: "enemy", classId: "x", role: "none", pos: { x: 2000, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 }),
    createActor({ id: ENEMY_B, kind: "enemy", classId: "x", role: "none", pos: { x: -2000, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 }),
  ];
  actors[1]!.health = 100;
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

function runUntil(state: SimState, byTick: Record<number, Command[]>, lastTick: number): SimEvent[] {
  const rules = makeRules(cfg());
  const events: SimEvent[] = [];
  while (state.tick <= lastTick) {
    rules(state, byTick[state.tick] ?? [], (e) => events.push(e));
    state.tick += 1;
  }
  return events;
}

const castCmd = (tick: number, abilityId: string, targetId?: number): Command =>
  targetId === undefined
    ? { tick, actorId: CASTER, kind: "cast", abilityId }
    : { tick, actorId: CASTER, kind: "cast", abilityId, targetId };
const targetCmd = (tick: number, targetId: number): Command => ({ tick, actorId: CASTER, kind: "target", targetId });

describe("a cast keeps the target it started on", () => {
  it("a bolt begun on A lands on A when the caster retargets B mid-cast", () => {
    const state = scenario();
    const events = runUntil(state, { 0: [castCmd(0, "bolt", ENEMY_A)], 7: [targetCmd(7, ENEMY_B)] }, 21);
    expect(events.some((e) => e.type === "cast_complete" && e.tick === 20 && e.abilityId === "bolt")).toBe(true);
    expect(state.actors[ENEMY_A]!.health).toBe(70);
    expect(state.actors[ENEMY_B]!.health).toBe(100);
    const opened = events.filter((e) => e.type === "window_opened");
    expect(opened.map((e) => e.targetId)).toEqual([ENEMY_A]);
    expect(state.actors[CASTER]!.targetId).toBe(ENEMY_B);
  });

  it("a heal begun on the tank heals the tank when the healer retargets an enemy mid-cast", () => {
    const state = scenario();
    const events = runUntil(state, { 0: [castCmd(0, "mend", ALLY)], 7: [targetCmd(7, ENEMY_A)] }, 21);
    expect(state.actors[ALLY]!.health).toBe(140);
    expect(events.some((e) => e.type === "heal" && e.targetId === ALLY && e.amount === 40)).toBe(true);
  });

  it("the next cast after a mid-cast retarget goes to the new target", () => {
    const state = scenario();
    const events = runUntil(
      state,
      { 0: [castCmd(0, "bolt", ENEMY_A)], 7: [targetCmd(7, ENEMY_B)], 25: [castCmd(25, "bolt", ENEMY_B)] },
      46,
    );
    expect(state.actors[ENEMY_A]!.health).toBe(70);
    expect(state.actors[ENEMY_B]!.health).toBe(70);
    expect(events.filter((e) => e.type === "window_opened").map((e) => e.targetId)).toEqual([ENEMY_A, ENEMY_B]);
  });

  it("leaves no cast-target bookkeeping in state once the cast is over (hashState keys unchanged)", () => {
    const retargeted = scenario();
    runUntil(retargeted, { 0: [castCmd(0, "bolt", ENEMY_A)], 7: [targetCmd(7, ENEMY_B)] }, 21);
    expect(Object.keys(retargeted.actors[CASTER]!)).not.toContain("castTargetId");
    const plain = scenario();
    runUntil(plain, { 0: [castCmd(0, "bolt", ENEMY_A)] }, 21);
    expect(Object.keys(plain.actors[CASTER]!).sort()).toEqual(Object.keys(retargeted.actors[CASTER]!).sort());
  });
});
