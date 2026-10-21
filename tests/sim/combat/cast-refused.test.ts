/**
 * CAST-REFUSED (f-fa598b6b): an enemy cast that combat refuses out_of_range (casting.ts startCast) is reported as a
 * cast_refused event {actorId, abilityId, reason}. Encounter-generated enemy casts are not in a trace record's
 * commands (game/sim/ORACLE.md section 3), so without the event a replay that runs no combat cannot tell a refused
 * cast from a started one. Players' presses are in the record and are never reported; other refusal reasons are not
 * reported either. The ability mirrors content/data/abilities/enemies.json seed.lash (target, range 1500,
 * castTicks 20). Twin of game/sim/Keyrun.Sim.Tests/CastRefusedEventTests.cs.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";

const ENEMY = 1;
const PLAYER = 2;

const lash: AbilityDef = {
  id: "seed.lash",
  castTicks: 20,
  gcdTicks: 0,
  cooldownTicks: 200,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 1500,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "damage", amount: 70, school: "physical" }],
};

function cfg(): RulesConfig {
  return {
    abilities: { "seed.lash": lash },
    effects: {},
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
  };
}

function scenario(enemyX: number, playerX: number): SimState {
  const actors = [
    createActor({ id: ENEMY, kind: "enemy", classId: "seed.elite", role: "none", pos: { x: enemyX, y: 0, z: 0 }, maxHealth: 3000, maxResource: 0 }),
    createActor({ id: PLAYER, kind: "player", classId: "warrior", role: "tank", pos: { x: playerX, y: 0, z: 0 }, maxHealth: 500, maxResource: 0 }),
  ];
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

function run(state: SimState, byTick: Record<number, Command[]>, lastTick: number): SimEvent[] {
  const rules = makeRules(cfg());
  const events: SimEvent[] = [];
  while (state.tick <= lastTick) {
    rules(state, byTick[state.tick] ?? [], (e) => events.push(e));
    state.tick += 1;
  }
  return events;
}

const cast = (tick: number, actorId: number, targetId: number): Command =>
  ({ tick, actorId, kind: "cast", abilityId: "seed.lash", targetId });
const refusals = (events: SimEvent[]): SimEvent[] => events.filter((e) => e.type === "cast_refused");

describe("an enemy cast refused out of range is reported as cast_refused", () => {
  it("an enemy lash out of range emits cast_refused and never starts", () => {
    const state = scenario(0, 2000);
    const events = run(state, { 0: [cast(0, ENEMY, PLAYER)] }, 0);
    expect(refusals(events)).toEqual([{ tick: 0, type: "cast_refused", actorId: ENEMY, abilityId: "seed.lash", reason: "out_of_range" }]);
    expect(state.actors[ENEMY]!.castId).toBeNull();
  });

  it("an enemy lash in range starts and emits no refusal", () => {
    const state = scenario(0, 1500);
    const events = run(state, { 0: [cast(0, ENEMY, PLAYER)] }, 0);
    expect(refusals(events)).toEqual([]);
    expect(state.actors[ENEMY]!.castId).toBe("seed.lash");
  });

  it("a player press refused out of range emits no refusal", () => {
    const state = scenario(2000, 0);
    const events = run(state, { 0: [cast(0, PLAYER, ENEMY)] }, 0);
    expect(refusals(events)).toEqual([]);
    expect(state.actors[PLAYER]!.castId).toBeNull();
  });

  it("an enemy lash refused not_ready emits no refusal", () => {
    const state = scenario(0, 1000);
    const events = run(state, { 0: [cast(0, ENEMY, PLAYER)], 26: [cast(26, ENEMY, PLAYER)] }, 26);
    expect(refusals(events)).toEqual([]);
    expect(state.actors[ENEMY]!.castId).toBeNull();
  });
});
