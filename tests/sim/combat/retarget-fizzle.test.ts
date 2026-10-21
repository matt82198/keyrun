/**
 * RETARGET-FIZZLE (finding f-f1ab879b, RULING R-RETARGET-1, the WoW rule): a cast whose start target dies mid-cast
 * fizzles; it does not hop to another target. The sim's dead-target auto-retarget (run-wiring.ts fixupTargeting) goes
 * through targeting.ts setTarget, so it pins the cast's start target like any other mid-cast retarget: the dead start
 * target resolves to nothing, cast_complete still fires, and no one takes the bolt. The auto-retarget still moves the
 * caster's current target to the next living enemy for the next press. Driven through makeRules with fixupTargeting
 * after every step, as stepRun runs it. Twin of game/sim/Keyrun.Sim.Tests/RetargetFizzleTests.cs.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";
import { fixupTargeting } from "../../../sim/run-wiring";

const CASTER = 1;
const TANK = 2;
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
  effects: [{ op: "damage", amount: 30, school: "arcane" }],
};

const smite: AbilityDef = {
  id: "smite",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "damage", amount: 100, school: "physical" }],
};

function cfg(): RulesConfig {
  return {
    abilities: { bolt, smite },
    effects: {},
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
  };
}

function scenario(): SimState {
  const actors = [
    createActor({ id: CASTER, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 200, maxResource: 0 }),
    createActor({ id: TANK, kind: "player", classId: "warden", role: "tank", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 200, maxResource: 0 }),
    createActor({ id: ENEMY_A, kind: "enemy", classId: "x", role: "none", pos: { x: 2000, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 }),
    createActor({ id: ENEMY_B, kind: "enemy", classId: "x", role: "none", pos: { x: -2000, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 }),
  ];
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

/** rules, then fixupTargeting, then the tick advance: stepRun's order (sim/index.ts). */
function runUntil(state: SimState, byTick: Record<number, Command[]>, lastTick: number): SimEvent[] {
  const rules = makeRules(cfg());
  const events: SimEvent[] = [];
  fixupTargeting(state);
  while (state.tick <= lastTick) {
    rules(state, byTick[state.tick] ?? [], (e) => events.push(e));
    fixupTargeting(state);
    state.tick += 1;
  }
  return events;
}

describe("a cast whose start target dies mid-cast fizzles (R-RETARGET-1)", () => {
  it("the bolt begun on A deals no damage to anyone when A dies mid-cast, and the caster's next target is B", () => {
    const state = scenario();
    const events = runUntil(
      state,
      {
        0: [{ tick: 0, actorId: CASTER, kind: "cast", abilityId: "bolt", targetId: ENEMY_A }],
        7: [{ tick: 7, actorId: TANK, kind: "cast", abilityId: "smite", targetId: ENEMY_A }],
      },
      21,
    );
    expect(state.actors[ENEMY_A]!.alive).toBe(false);
    expect(events.some((e) => e.type === "cast_complete" && e.actorId === CASTER && e.abilityId === "bolt")).toBe(true);
    expect(events.filter((e) => e.type === "damage" && e.sourceId === CASTER)).toEqual([]);
    expect(state.actors[ENEMY_B]!.health).toBe(100);
    expect(state.actors[CASTER]!.targetId).toBe(ENEMY_B);
    expect(Object.keys(state.actors[CASTER]!)).not.toContain("castTargetId");
  });
});
