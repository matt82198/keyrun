/**
 * DEATH-CANCELS-CAST (R-DEADCAST-1, finding f-42df9d4f; WoW: death cancels every cast in progress; BIBLE s3 "cast
 * bars are the enemy's language"). An actor killed mid-cast has that cast ended on its death tick with cast_stopped:
 * no cast_complete fires for it and its damage or heal never lands. Controls: a caster that survives completes the
 * same cast, and a cast a dead actor starts AFTER its death (the onDeath DEATH-CAST EXCEPTION, rules.ts) still
 * completes. Driven through makeRules. Twin of game/sim/Keyrun.Sim.Tests/DeathCancelsCastTests.cs
 * (Damage.EndCastOnDeath).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";

const CASTER = 1;
const ALLY = 2;
const FOE = 10;
const KILLER = 11;
const KILL_TICK = 10; // inside the 20-tick cast begun at 0: it would land at 20

const base = {
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 50000,
  shape: { kind: "target" },
  interruptible: false,
} as const;
const bolt: AbilityDef = { ...base, id: "bolt", castTicks: 20, effects: [{ op: "damage", amount: 30, school: "arcane" }] };
const mend: AbilityDef = { ...base, id: "mend", castTicks: 20, effects: [{ op: "heal", amount: 40 }] };
const nuke: AbilityDef = { ...base, id: "nuke", castTicks: 0, effects: [{ op: "damage", amount: 1000, school: "physical" }] };

function cfg(): RulesConfig {
  return {
    abilities: { bolt, mend, nuke },
    effects: {},
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
  };
}

function scenario(casterHealth: number): SimState {
  const actors = [
    createActor({ id: CASTER, kind: "player", classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: 0 }, maxHealth: casterHealth, maxResource: 0 }),
    createActor({ id: ALLY, kind: "player", classId: "warden", role: "tank", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 200, maxResource: 0 }),
    createActor({ id: FOE, kind: "enemy", classId: "x", role: "none", pos: { x: 2000, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 }),
    createActor({ id: KILLER, kind: "enemy", classId: "x", role: "none", pos: { x: -2000, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 }),
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

const cast = (tick: number, actorId: number, abilityId: string, targetId: number): Command =>
  ({ tick, actorId, kind: "cast", abilityId, targetId });
const kill = { [KILL_TICK]: [cast(KILL_TICK, KILLER, "nuke", CASTER)] };
const completes = (events: SimEvent[], id: string): SimEvent[] =>
  events.filter((e) => e.type === "cast_complete" && e.actorId === CASTER && e.abilityId === id);

describe("death cancels the dying actor's cast in progress", () => {
  it("a bolt from a caster killed mid-cast never completes and never lands", () => {
    const state = scenario(200);
    const events = runUntil(state, { 0: [cast(0, CASTER, "bolt", FOE)], ...kill }, 30);
    expect(events.some((e) => e.type === "death" && e.actorId === CASTER && e.tick === KILL_TICK)).toBe(true);
    expect(completes(events, "bolt")).toEqual([]);
    expect(state.actors[FOE]!.health).toBe(100);
    const stopped = events.filter((e) => e.type === "cast_stopped" && e.actorId === CASTER);
    expect(stopped.map((e) => [e.tick, e.abilityId])).toEqual([[KILL_TICK, "bolt"]]);
    expect(state.actors[CASTER]!.castId).toBeNull();
  });

  it("a heal from a caster killed mid-cast never lands", () => {
    const state = scenario(200);
    const events = runUntil(state, { 0: [cast(0, CASTER, "mend", ALLY)], ...kill }, 30);
    expect(completes(events, "mend")).toEqual([]);
    expect(state.actors[ALLY]!.health).toBe(100);
  });

  it("control: a caster that survives the hit completes the same bolt", () => {
    const state = scenario(5000);
    const events = runUntil(state, { 0: [cast(0, CASTER, "bolt", FOE)], ...kill }, 30);
    expect(completes(events, "bolt").map((e) => e.tick)).toEqual([20]);
    expect(state.actors[FOE]!.health).toBe(70);
    expect(events.some((e) => e.type === "cast_stopped")).toBe(false);
  });

  it("control: a cast a dead actor starts after its death (DEATH-CAST EXCEPTION) still completes", () => {
    const state = scenario(200);
    const events = runUntil(state, { ...kill, 11: [cast(11, CASTER, "bolt", FOE)] }, 40);
    expect(state.actors[CASTER]!.alive).toBe(false);
    expect(completes(events, "bolt").map((e) => e.tick)).toEqual([31]);
    expect(state.actors[FOE]!.health).toBe(70);
  });
});
