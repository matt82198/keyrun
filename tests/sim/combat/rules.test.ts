/**
 * Integration tests for the composed StepRules (sim/combat/rules.ts): movement, instant casts,
 * periodic effect ticks, and collision push-out / bounds clamping. rules() never advances
 * state.tick itself -- sim/core/tick.ts's step() is the single owner of the tick (per
 * sim/CLAUDE.md); a caller driving rules() directly, as these tests do, must advance state.tick
 * itself between calls, exactly as step() does (see the `tick` helper below).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import type { EffectDef } from "../../../sim/combat/effects";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";

const poke: AbilityDef = {
  id: "poke",
  castTicks: 0,
  gcdTicks: 15,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: true,
  effects: [{ op: "applyEffect", effectId: "dot", durationTicks: 40, stacks: 1 }],
};

const dot: EffectDef = {
  id: "dot",
  maxStacks: 1,
  dispellableTag: "magic",
  modifiers: {},
  periodic: { everyTicks: 20, op: { op: "damage", amount: 10, school: "nature" } },
};

function baseCfg(): RulesConfig {
  return {
    abilities: { poke },
    effects: { dot },
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -1000, maxX: 1000, minZ: -1000, maxZ: 1000 },
  };
}

function stateWith(actors: ReturnType<typeof createActor>[]): SimState {
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

/** rules() itself never advances state.tick; this helper does it the way sim/core/tick.ts's
 * step() would, so tests that drive several ticks in a row read the same as before. */
function tick(state: SimState, rules: ReturnType<typeof makeRules>, cmds: Command[], emit: (e: SimEvent) => void): void {
  rules(state, cmds, emit);
  state.tick += 1;
}

describe("movement command", () => {
  it("moves the actor and does not itself advance the tick", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    const state = stateWith([a]);
    const rules = makeRules(baseCfg());
    const cmd: Command = { tick: 0, actorId: 1, kind: "move", dir: { x: 1, z: 0 }, yaw: 0 };
    rules(state, [cmd], () => {});
    expect(state.actors[1]!.pos.x).toBe(300);
    // sim/core/tick.ts's step() is the single owner of the tick; rules() must not advance it.
    expect(state.tick).toBe(0);
  });
});

describe("instant cast command", () => {
  it("completes within the same step and applies its effect", () => {
    const caster = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = stateWith([caster, target]);
    const rules = makeRules(baseCfg());
    const events: SimEvent[] = [];
    const cmd: Command = { tick: 0, actorId: 1, kind: "cast", abilityId: "poke", targetId: 2 };
    rules(state, [cmd], (e) => events.push(e));
    expect(target.effects).toHaveLength(1);
    expect(events.some((e) => e.type === "cast_complete")).toBe(true);
  });
});

describe("periodic effects", () => {
  it("ticks a dot for its configured damage every everyTicks and expires it after duration", () => {
    const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = stateWith([target]);
    target.effects.push({ effectId: "dot", sourceId: 1, stacks: 1, endsTick: 45, nextPeriodicTick: 20 });
    const rules = makeRules(baseCfg());
    const events: SimEvent[] = [];
    // rules() checks periodics due at state.tick before the `tick` helper advances it (mirroring
    // sim/core/tick.ts's step()), so the call that processes tick 20 (and fires the dot) is the
    // 21st call.
    for (let i = 0; i < 21; i += 1) tick(state, rules, [], (e) => events.push(e));
    expect(state.tick).toBe(21);
    expect(target.health).toBe(90);
    expect(target.effects[0]!.nextPeriodicTick).toBe(40);
  });
});

describe("collision and bounds", () => {
  it("pushes overlapping actors apart and clamps positions to arena bounds", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: -990, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    const b = createActor({ id: 2, kind: "player", classId: "x", role: "damage", pos: { x: -1050, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    const state = stateWith([a, b]);
    const rules = makeRules(baseCfg());
    rules(state, [], () => {});
    expect(a.pos.x).toBeLessThanOrEqual(1000);
    expect(a.pos.x).toBeGreaterThanOrEqual(-1000);
    expect(b.pos.x).toBeGreaterThanOrEqual(-1000);
    expect(a.pos.x).not.toBe(-990); // pushed apart from overlap
  });
});
