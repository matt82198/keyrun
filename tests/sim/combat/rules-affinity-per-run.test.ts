/**
 * tests/sim/combat/rules-affinity-per-run.test.ts — fix/makerules-closure-affinity: a StepRules
 * closure returned by makeRules (sim/combat/rules.ts) must classify circle/cone/line targets with
 * ITS OWN run's effect table, never with whichever run last called makeRules.
 *
 * Before the fix, makeRules called configureAffinityEffects(cfg.effects) once at closure-BUILD
 * time, writing sim/combat/affinity.ts's module-level registry; the closure itself never touched it
 * again. Two runs whose effect tables give the SAME effect id OPPOSITE polarity therefore leaked
 * into each other as soon as a closure was invoked after a later makeRules call: run A's friendly
 * pulse resolved as hostile (marking the enemy, not the party) because run B's table was live.
 *
 * Two paths are driven, both with two runs created in sequence and stepped interleaved:
 *  1. The public host path (createRun/stepRun, one run per affinity setup, one via a loadout).
 *     stepRun rebuilds makeRules every tick, so this path was already safe; kept as a guard.
 *  2. Held StepRules closures (makeRules once per run, then sim/core/tick.step), the shape
 *     tests/bots/floor-telegraph-loop.test.ts and several sim suites drive. This is the one that
 *     reddens on the pre-fix code.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimState } from "@sim/core/state";
import { hashState } from "@sim/core/state";
import { step as coreStep } from "@sim/core/tick";
import type { AbilityDef } from "@sim/combat/abilities";
import type { EffectDef } from "@sim/combat/effects";
import type { DungeonDef } from "@sim/encounter/pulls";
import { makeRules } from "@sim/combat/rules";
import { rulesConfigOf } from "@sim/run-wiring";
import type { StepRules } from "@sim/combat/types";

const SEED = 9191;
const ORIGIN = { x: 0, y: 0, z: 0 };
const BOUNDS = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };
const PLAYER = 1;
const PARTNER = 2;
const ENEMY = 100;

function circleApply(effectId: string): AbilityDef {
  return {
    id: "pulse",
    castTicks: 0,
    gcdTicks: 0,
    cooldownTicks: 0,
    charges: 1,
    cost: { resource: "energy", amount: 0 },
    range: 100000,
    shape: { kind: "circle", radius: 5000 },
    interruptible: false,
    effects: [{ op: "applyEffect", effectId, durationTicks: 100, stacks: 1 }],
  };
}

const markDef = (damageTakenBp: number): EffectDef => ({ id: "mark", maxStacks: 1, dispellableTag: null, modifiers: { damageTakenBp }, periodic: null });

const DUNGEON: DungeonDef = {
  id: "one-pull",
  pulls: [{ id: "boss", spawns: [{ actorId: ENEMY, classId: "boss", pos: ORIGIN, yaw: 0, health: 1000000 }] }],
};
const PARTY: PartyMemberSpec[] = [
  { id: PLAYER, classId: "alpha", role: "damage", pos: ORIGIN, maxHealth: 200, maxResource: 100 },
  { id: PARTNER, classId: "beta", role: "damage", pos: ORIGIN, maxHealth: 200, maxResource: 100 },
];

function content(effects: Record<string, EffectDef>, abilities: Record<string, AbilityDef> = { pulse: circleApply("mark") }): RunContent {
  return { abilities, effects, lockoutTicks: 40, baseSpeedMmPerTick: 180, actorRadiusMm: 400, bounds: BOUNDS, enemyScripts: {} };
}

/** "mark" is beneficial here: the pulse is friendly and lands on the party (caster included). */
const BUFF = content({ mark: markDef(-1000) });
/** "mark" is harmful here: the same pulse id is hostile and lands on the enemy only. */
const CURSE = content({ mark: markDef(1000) });
/** Both "mark" (harmful) and "ward" (beneficial) exist; which one the pulse applies is the loadout's call. */
const MIXED = content({ mark: markDef(1000), ward: { ...markDef(-1000), id: "ward" } });
const WARD_LOADOUT = { [PLAYER]: { pulse: circleApply("ward") } };

const PULSE_AT: Command[] = [
  { tick: 10, actorId: PLAYER, kind: "cast", abilityId: "pulse" },
  { tick: 20, actorId: PLAYER, kind: "cast", abilityId: "pulse" },
];

const holding = (state: SimState, effectId: string): number[] =>
  Object.values(state.actors)
    .filter((a) => a.effects.some((e) => e.effectId === effectId))
    .map((a) => a.id)
    .sort((x, y) => x - y);

const at = (tick: number): Command[] => PULSE_AT.filter((c) => c.tick === tick);

/** Steps every run one tick at a time, round-robin, through the public stepRun path. */
function interleaveStepRun(runs: Run[], ticks: number): Run[] {
  const out = [...runs];
  for (let t = 0; t < ticks; t++) out.forEach((run, i) => (out[i] = stepRun(run, at(run.state.tick)).run));
  return out;
}

/** Same, but each run keeps ONE StepRules closure built up front (all built before any step). */
function interleaveHeld(runs: Run[], ticks: number): SimState[] {
  const rules: StepRules[] = runs.map((r) => makeRules(rulesConfigOf(r.content)));
  const states = runs.map((r) => r.state);
  for (let t = 0; t < ticks; t++) states.forEach((s, i) => (states[i] = coreStep(s, at(s.tick), rules[i]!).state));
  return states;
}

describe("makeRules closure: affinity effects are bound to the run, not module state", () => {
  it("public path: two sequentially created runs with opposite 'mark' polarity, interleaved", () => {
    const [buff, curse] = interleaveStepRun([createRun(SEED, PARTY, DUNGEON, BUFF), createRun(SEED, PARTY, DUNGEON, CURSE)], 30);
    expect(holding(buff!.state, "mark")).toEqual([PLAYER, PARTNER]);
    expect(holding(curse!.state, "mark")).toEqual([ENEMY]);
    const [soloBuff] = interleaveStepRun([createRun(SEED, PARTY, DUNGEON, BUFF)], 30);
    const [soloCurse] = interleaveStepRun([createRun(SEED, PARTY, DUNGEON, CURSE)], 30);
    expect(hashState(buff!.state)).toBe(hashState(soloBuff!.state));
    expect(hashState(curse!.state)).toBe(hashState(soloCurse!.state));
  });

  it("public path: a loadout-patched run and a plain run over the same content, interleaved", () => {
    const [ward, plain] = interleaveStepRun(
      [createRun(SEED, PARTY, DUNGEON, MIXED, { loadouts: WARD_LOADOUT }), createRun(SEED, PARTY, DUNGEON, MIXED)],
      30,
    );
    expect(holding(ward!.state, "ward")).toEqual([PLAYER, PARTNER]);
    expect(holding(ward!.state, "mark")).toEqual([]);
    expect(holding(plain!.state, "mark")).toEqual([ENEMY]);
    expect(holding(plain!.state, "ward")).toEqual([]);
  });

  it("held closures: run A's StepRules keeps A's effects after run B's makeRules (A friendly, B hostile)", () => {
    const [buff, curse] = interleaveHeld([createRun(SEED, PARTY, DUNGEON, BUFF), createRun(SEED, PARTY, DUNGEON, CURSE)], 30);
    expect(holding(buff!, "mark")).toEqual([PLAYER, PARTNER]);
    expect(holding(curse!, "mark")).toEqual([ENEMY]);
  });

  it("held closures: order reversed (A hostile, B friendly) so neither creation order hides a leak", () => {
    const [curse, buff] = interleaveHeld([createRun(SEED, PARTY, DUNGEON, CURSE), createRun(SEED, PARTY, DUNGEON, BUFF)], 30);
    expect(holding(curse!, "mark")).toEqual([ENEMY]);
    expect(holding(buff!, "mark")).toEqual([PLAYER, PARTNER]);
  });

  it("held closures, interleaved, match each run's solo held replay hash-for-hash", () => {
    const held = interleaveHeld([createRun(SEED, PARTY, DUNGEON, BUFF), createRun(SEED, PARTY, DUNGEON, CURSE)], 30);
    const [soloBuff] = interleaveHeld([createRun(SEED, PARTY, DUNGEON, BUFF)], 30);
    const [soloCurse] = interleaveHeld([createRun(SEED, PARTY, DUNGEON, CURSE)], 30);
    expect(hashState(held[0]!)).toBe(hashState(soloBuff!));
    expect(hashState(held[1]!)).toBe(hashState(soloCurse!));
  });
});
