/**
 * Scenario tests for interrupt-vs-completion timing, driven through rules.ts tick by tick (the
 * behavior the whole sim-combat contract hinges on):
 *   - caster starts a 30-tick cast at tick 10 (castEndsTick = 40);
 *   - an interrupt landing at tick 39 (strictly before 40) wins and locks the school;
 *   - the same interrupt arriving at tick 40 instead is too late: the cast completes first,
 *     because rules.ts always finishes due casts before processing that tick's commands.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";

const fireball: AbilityDef = {
  id: "fireball",
  castTicks: 30,
  gcdTicks: 15,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "mana", amount: 20 },
  range: 30000,
  shape: { kind: "target" },
  interruptible: true,
  effects: [{ op: "damage", amount: 100, school: "fire" }],
};

const kick: AbilityDef = {
  id: "kick",
  castTicks: 0,
  gcdTicks: 15,
  cooldownTicks: 40,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: false,
  effects: [{ op: "interrupt" }],
};

function setup(): { state: SimState; rules: ReturnType<typeof makeRules>; events: SimEvent[] } {
  const caster = createActor({ id: 1, kind: "player", classId: "mage", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
  const dummy = createActor({ id: 2, kind: "enemy", classId: "target-dummy", role: "none", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 500, maxResource: 0 });
  const rogue = createActor({ id: 3, kind: "player", classId: "rogue", role: "damage", pos: { x: 0, y: 0, z: 100 }, maxHealth: 100, maxResource: 100 });
  const state: SimState = {
    tick: 0,
    seed: 1,
    prng: [0, 0, 0, 0],
    actors: { 1: caster, 2: dummy, 3: rogue },
    runTimerTicks: 0,
    phase: "combat",
    log: [],
  };
  const cfg: RulesConfig = {
    abilities: { fireball, kick },
    effects: {},
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
  };
  const events: SimEvent[] = [];
  const rules = makeRules(cfg);
  return { state, rules, events };
}

function runUntil(state: SimState, rules: ReturnType<typeof makeRules>, events: SimEvent[], commandsByTick: Map<number, Command[]>, lastTick: number): void {
  while (state.tick <= lastTick) {
    const cmds = commandsByTick.get(state.tick) ?? [];
    rules(state, cmds, (e) => events.push(e));
  }
}

describe("interrupt strictly before completion", () => {
  it("landing at tick 39 (< castEndsTick 40) cancels the cast and locks the school", () => {
    const { state, rules, events } = setup();
    const commands = new Map<number, Command[]>([
      [10, [{ tick: 10, actorId: 1, kind: "cast", abilityId: "fireball", targetId: 2 }]],
      [39, [{ tick: 39, actorId: 3, kind: "cast", abilityId: "kick", targetId: 1 }]],
    ]);
    runUntil(state, rules, events, commands, 41);

    const caster = state.actors[1]!;
    const dummy = state.actors[2]!;
    expect(caster.castId).toBeNull();
    expect(dummy.health).toBe(500); // fireball never landed
    expect(caster.effects).toContainEqual(expect.objectContaining({ effectId: "lockout:fire" }));
    expect(events.some((e) => e.type === "interrupt" && e.tick === 39)).toBe(true);
    expect(events.some((e) => e.type === "cast_complete" && e.abilityId === "fireball")).toBe(false);
  });
});

describe("interrupt arriving exactly at completion", () => {
  it("landing at tick 40 (=== castEndsTick) is too late: the cast completes and the interrupt is a no-op", () => {
    const { state, rules, events } = setup();
    const commands = new Map<number, Command[]>([
      [10, [{ tick: 10, actorId: 1, kind: "cast", abilityId: "fireball", targetId: 2 }]],
      [40, [{ tick: 40, actorId: 3, kind: "cast", abilityId: "kick", targetId: 1 }]],
    ]);
    runUntil(state, rules, events, commands, 41);

    const caster = state.actors[1]!;
    const dummy = state.actors[2]!;
    expect(dummy.health).toBe(400); // fireball landed for 100
    expect(caster.effects.some((e) => e.effectId.startsWith("lockout:"))).toBe(false);
    expect(events.some((e) => e.type === "cast_complete" && e.tick === 40)).toBe(true);
    expect(events.some((e) => e.type === "interrupt")).toBe(false);
  });
});
