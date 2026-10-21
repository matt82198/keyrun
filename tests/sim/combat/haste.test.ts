/**
 * R-HASTE-1: haste from every live effect shortens casts, as Bloodlust does in WoW. The TS cast path mirrors C#
 * Casting.HastedCastTicks (game/sim/Keyrun.Sim/Combat/Casting.cs): castTicks * 10000 / (10000 + hasteBp), floored,
 * hasteBp summed over the caster's effects with endsTick > now (times stacks). An instant stays instant; no effects
 * map, zero haste, or a total at or below -10000 leaves the content cast time unchanged.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import type { EffectDef } from "../../../sim/combat/effects";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { ActorState, SimState } from "../../../sim/combat/types";

const bolt: AbilityDef = {
  id: "bolt",
  castTicks: 20,
  gcdTicks: 15,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "mana", amount: 0 },
  range: 30000,
  shape: { kind: "target" },
  interruptible: true,
  effects: [{ op: "damage", amount: 10, school: "fire" }],
};

const hasted: EffectDef = { id: "hasted", maxStacks: 1, dispellableTag: null, modifiers: { hasteBp: 1500 }, periodic: null };
const chill: EffectDef = { id: "chill", maxStacks: 1, dispellableTag: "magic", modifiers: { hasteBp: -3000 }, periodic: null };
const frenzy: EffectDef = { id: "frenzy", maxStacks: 5, dispellableTag: null, modifiers: { hasteBp: 1000 }, periodic: null };
const stall: EffectDef = { id: "stall", maxStacks: 1, dispellableTag: null, modifiers: { hasteBp: -10000 }, periodic: null };

function cfg(): RulesConfig {
  return {
    abilities: { bolt },
    effects: { hasted, chill, frenzy, stall },
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
  };
}

function setup(effects: { id: string; stacks?: number; endsTick?: number }[]): { state: SimState; caster: ActorState } {
  const caster = createActor({ id: 1, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 100 });
  const target = createActor({ id: 2, kind: "enemy", classId: "x", role: "none", pos: { x: 1000, y: 0, z: 0 }, maxHealth: 1000, maxResource: 0 });
  for (const e of effects) {
    caster.effects.push({ effectId: e.id, sourceId: 1, stacks: e.stacks ?? 1, endsTick: e.endsTick ?? 500, nextPeriodicTick: null });
  }
  const state: SimState = { tick: 100, seed: 1, prng: [0, 0, 0, 0], actors: { 1: caster, 2: target }, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, caster };
}

function castEnd(effects: { id: string; stacks?: number; endsTick?: number }[], ability: AbilityDef = bolt): number | null {
  const { state, caster } = setup(effects);
  const c = cfg();
  c.abilities = { [ability.id]: ability };
  makeRules(c)(state, [{ tick: 100, actorId: 1, kind: "cast", abilityId: ability.id, targetId: 2 }], () => {});
  return caster.castEndsTick;
}

describe("R-HASTE-1: cast time under haste (mirrors Casting.HastedCastTicks)", () => {
  it("leaves the cast time unchanged with no haste", () => {
    expect(castEnd([])).toBe(120);
  });

  it("Anthem (hasted, +1500 bp) shortens a 20-tick cast to 17 (floor of 200000 / 11500)", () => {
    expect(castEnd([{ id: "hasted" }])).toBe(117);
  });

  it("Chill (-3000 bp) lengthens a 20-tick cast to 28 (BIBLE s5)", () => {
    expect(castEnd([{ id: "chill" }])).toBe(128);
  });

  it("sums every live effect and multiplies by stacks (Anthem + 3 Frenzy = +4500 bp -> 13)", () => {
    expect(castEnd([{ id: "hasted" }, { id: "frenzy", stacks: 3 }])).toBe(113);
  });

  it("ignores an effect whose endsTick is not after now", () => {
    expect(castEnd([{ id: "hasted", endsTick: 100 }])).toBe(120);
  });

  it("leaves the cast time unchanged when the total is at or below -10000 bp", () => {
    expect(castEnd([{ id: "stall" }])).toBe(120);
  });
});
