/**
 * Regression coverage for two sim correctness defects found by the bots pull-1 lane (PR #87):
 *
 * A. Inert mitigation: every damage call site (casting.ts completeCast, combos.ts
 *    applyComboEffects, rules.ts's periodic DoT tick) passed 0,0 as applyDamage's modifier
 *    arguments, so damageTakenBp effects (Brace -4000 at content/data/effects/player.json:19,
 *    Bulwark's braced -1500 at :15, Vigil's bracedSelf -3000 at :17, ...) and damageDoneBp
 *    effects never changed a hit.
 * B. Resource credited to the target: a "resource" op (Warden momentum builder, Cinder heat,
 *    the Forge combo's +40) moved the TARGET's resource instead of the caster's, so the caster
 *    could never afford Vigil (50) / Reckoning (100). EffectOp's "resource" variant
 *    (sim/combat/abilities.ts) has no target field, so the caster is the only recipient; it is
 *    credited once per completed cast, not once per resolved target.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import type { EffectDef } from "../../../sim/combat/effects";
import { completeCast, startCast, type CastContext } from "../../../sim/combat/casting";
import { applyComboEffects } from "../../../sim/combat/combos";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { SimEvent, SimState } from "../../../sim/combat/types";

/** Mirrors content/data/effects/player.json:18-19 ("braceSelf", Brace, damageTakenBp -4000). */
const braceSelf: EffectDef = { id: "braceSelf", maxStacks: 1, dispellableTag: null, modifiers: { damageTakenBp: -4000 }, periodic: null };
/** A +50% damage-done buff: the sim's EffectModifiers.damageDoneBp (sim/combat/effects.ts). */
const empowered: EffectDef = { id: "empowered", maxStacks: 1, dispellableTag: null, modifiers: { damageDoneBp: 5000 }, periodic: null };
const burn: EffectDef = {
  id: "burn",
  maxStacks: 1,
  dispellableTag: null,
  modifiers: {},
  periodic: { everyTicks: 20, op: { op: "damage", amount: 70, school: "fire" } },
};
const effects: Record<string, EffectDef> = { braceSelf, empowered, burn };

function ability(id: string, shape: AbilityDef["shape"], ops: AbilityDef["effects"]): AbilityDef {
  return {
    id,
    castTicks: 0,
    gcdTicks: 0,
    cooldownTicks: 0,
    charges: 1,
    cost: { resource: "none", amount: 0 },
    range: 30000,
    shape,
    interruptible: false,
    effects: ops,
  };
}

const slam = ability("slam", { kind: "target" }, [{ op: "damage", amount: 70, school: "physical" }]);
/** warden.strike's shape (content/data/abilities/warden.json:24-25: resource delta 15). */
const strike = ability("strike", { kind: "target" }, [
  { op: "damage", amount: 10, school: "physical" },
  { op: "resource", delta: 15 },
]);
const sweep = ability("sweep", { kind: "circle", radius: 5000 }, [
  { op: "damage", amount: 10, school: "physical" },
  { op: "resource", delta: 15 },
]);

function mk(id: number, kind: "player" | "enemy", maxResource = 100) {
  const a = createActor({ id, kind, classId: kind === "player" ? "warden" : "mob", role: kind === "player" ? "tank" : "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource });
  a.resource = 0;
  return a;
}

function stateOf(actors: ReturnType<typeof mk>[]): SimState {
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

function ctx(): CastContext {
  return { abilities: { slam, strike, sweep }, effects, lockoutTicks: 60 };
}

function castNow(state: SimState, caster: ReturnType<typeof mk>, def: AbilityDef, targetId: number | undefined): SimEvent[] {
  const events: SimEvent[] = [];
  expect(startCast(state, caster, def, targetId, undefined, state.tick)).toBeNull();
  completeCast(state, caster, ctx(), (e) => events.push(e));
  return events;
}

function brace(a: ReturnType<typeof mk>, sourceId: number): void {
  a.effects.push({ effectId: "braceSelf", sourceId, stacks: 1, endsTick: 1000, nextPeriodicTick: null });
}

describe("defect A: damage modifiers reach applyDamage", () => {
  it("casting: a Braced actor hit for 70 loses 42 (70 * (10000-4000)/10000)", () => {
    const enemy = mk(1, "enemy");
    const tank = mk(2, "player");
    brace(tank, 2);
    castNow(stateOf([enemy, tank]), enemy, slam, 2);
    expect(1000 - tank.health).toBe(42);
  });

  it("casting: the attacker's damageDoneBp scales the hit (70 * 1.5 = 105)", () => {
    const enemy = mk(1, "enemy");
    const tank = mk(2, "player");
    enemy.effects.push({ effectId: "empowered", sourceId: 1, stacks: 1, endsTick: 1000, nextPeriodicTick: null });
    castNow(stateOf([enemy, tank]), enemy, slam, 2);
    expect(1000 - tank.health).toBe(105);
  });

  it("combos: a combo's damage op respects the target's Brace", () => {
    const payoff = mk(1, "player");
    const target = mk(2, "enemy");
    brace(target, 2);
    applyComboEffects(stateOf([payoff, target]), payoff, target, [{ op: "damage", amount: 70, school: "fire" }], effects, () => {});
    expect(1000 - target.health).toBe(42);
  });

  it("rules: a periodic DoT tick respects the carrier's Brace", () => {
    const enemy = mk(1, "enemy");
    const tank = mk(2, "player");
    brace(tank, 2);
    tank.effects.push({ effectId: "burn", sourceId: 1, stacks: 1, endsTick: 1000, nextPeriodicTick: 0 });
    const cfg: RulesConfig = {
      abilities: { slam },
      effects,
      lockoutTicks: 60,
      baseSpeedMmPerTick: 300,
      actorRadiusMm: 0,
      bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
    };
    makeRules(cfg)(stateOf([enemy, tank]), [], () => {});
    expect(1000 - tank.health).toBe(42);
  });
});

describe("defect B: resource ops credit the caster", () => {
  it("a Warden's momentum builder on an enemy raises the Warden's momentum, not the enemy's", () => {
    const warden = mk(1, "player");
    const enemy = mk(2, "enemy");
    castNow(stateOf([warden, enemy]), warden, strike, 2);
    expect(warden.resource).toBe(15);
    expect(enemy.resource).toBe(0);
  });

  it("an AoE builder hitting two enemies credits the caster once per cast", () => {
    const warden = mk(1, "player");
    const e1 = mk(2, "enemy");
    const e2 = mk(3, "enemy");
    const state = stateOf([warden, e1, e2]);
    const cfg: RulesConfig = {
      abilities: { sweep },
      effects,
      lockoutTicks: 60,
      baseSpeedMmPerTick: 300,
      actorRadiusMm: 0,
      bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
    };
    makeRules(cfg)(state, [{ tick: 0, actorId: 1, kind: "cast", abilityId: "sweep" }], () => {});
    expect(e1.health).toBe(990);
    expect(e2.health).toBe(990);
    expect(warden.resource).toBe(15);
    expect(e1.resource + e2.resource).toBe(0);
  });

  it("combos: the Forge combo's +40 credits the payoff actor, not the Window carrier", () => {
    const payoff = mk(1, "player");
    const carrier = mk(2, "enemy");
    applyComboEffects(stateOf([payoff, carrier]), payoff, carrier, [{ op: "resource", delta: 40 }], effects, () => {});
    expect(payoff.resource).toBe(40);
    expect(carrier.resource).toBe(0);
  });
});
