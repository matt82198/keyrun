/**
 * Windows and Combos (design/BIBLE.md §4/§5) integration through StepRules: open -> payoff by an
 * other class fires the right combo and effects; same-class payoff is denied; a Window that is
 * never paid off expires; the floor "field" variant closes for whoever stands in it.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";
import type { ComboTable } from "../../../sim/combat/combos";

const setupAbility: AbilityDef = {
  id: "sundered_strike",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: false,
  tags: ["setup"],
  effects: [{ op: "window", windowId: "sundered", durationTicks: 60, onTarget: true, setupClass: "warden" }],
};

const payoffAbility: AbilityDef = {
  id: "detonate",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 5000,
  shape: { kind: "target" },
  interruptible: false,
  tags: ["payoff"],
  effects: [{ op: "payoff" }],
};

const floorSetupAbility: AbilityDef = {
  id: "bulwark",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 0,
  shape: { kind: "self" },
  interruptible: false,
  tags: ["setup"],
  effects: [{ op: "window", windowId: "bulwark-patch", durationTicks: 60, onTarget: false, setupClass: "warden" }],
};

const combos: ComboTable = {
  warden: {
    cinder: { id: "shatter", effects: [{ op: "damage", amount: 50, school: "physical" }] },
    chanter: { id: "consecrate", effects: [{ op: "damage", amount: 30, school: "physical" }] },
  },
};

const fieldCombos: ComboTable = {
  warden: {
    cinder: { id: "shatter-field", effects: [{ op: "damage", amount: 10, school: "physical" }] },
  },
};

function baseCfg(overrides: Partial<RulesConfig> = {}): RulesConfig {
  return {
    abilities: { sundered_strike: setupAbility, detonate: payoffAbility, bulwark: floorSetupAbility },
    effects: {},
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -10000, maxX: 10000, minZ: -10000, maxZ: 10000 },
    combos,
    fieldCombos,
    ...overrides,
  };
}

function stateWith(actors: ReturnType<typeof createActor>[]): SimState {
  const record: SimState["actors"] = {};
  for (const a of actors) record[a.id] = a;
  return { tick: 0, seed: 1, prng: [0, 0, 0, 0], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
}

function cast(actorId: number, abilityId: string, targetId: number): Command {
  return { tick: 0, actorId, kind: "cast", abilityId, targetId };
}

// rules() itself never advances state.tick -- sim/core/tick.ts's step() is the single owner of the
// tick (per sim/CLAUDE.md). This suite drives rules() directly (not through step()), so it must
// advance state.tick itself between calls, exactly as step() does.
function tick(state: SimState, rules: ReturnType<typeof makeRules>, cmds: Command[], emit: (e: SimEvent) => void): void {
  rules(state, cmds, emit);
  state.tick += 1;
}

describe("Window open -> Payoff by another class", () => {
  it("fires the looked-up Combo and applies its effects", () => {
    const warden = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const cinder = createActor({ id: 2, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const boss = createActor({ id: 3, kind: "enemy", classId: "boss", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource: 0 });
    const state = stateWith([warden, cinder, boss]);
    const rules = makeRules(baseCfg());
    const events: SimEvent[] = [];
    const emit = (e: SimEvent) => events.push(e);

    tick(state, rules, [cast(1, "sundered_strike", 3)], emit);
    expect(events.some((e) => e.type === "window_opened" && e.windowId === "sundered")).toBe(true);
    expect(boss.effects.some((e) => e.effectId.startsWith("window:sundered:"))).toBe(true);

    tick(state, rules, [cast(2, "detonate", 3)], emit);

    const combo = events.find((e) => e.type === "combo");
    expect(combo).toBeDefined();
    expect(combo?.comboId).toBe("shatter");
    expect(combo?.setupActorId).toBe(1);
    expect(combo?.payoffActorId).toBe(2);
    expect(combo?.windowId).toBe("sundered");
    expect(boss.health).toBe(950); // 1000 - 50 combo damage
    expect(boss.effects.some((e) => e.effectId.startsWith("window:"))).toBe(false); // window consumed
  });
});

describe("same-class Payoff", () => {
  it("does nothing to the Window and emits combo_denied", () => {
    const warden1 = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const warden2 = createActor({ id: 2, kind: "player", classId: "warden", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const boss = createActor({ id: 3, kind: "enemy", classId: "boss", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource: 0 });
    const state = stateWith([warden1, warden2, boss]);
    const rules = makeRules(baseCfg());
    const events: SimEvent[] = [];
    const emit = (e: SimEvent) => events.push(e);

    tick(state, rules, [cast(1, "sundered_strike", 3)], emit);
    tick(state, rules, [cast(2, "detonate", 3)], emit);

    expect(events.some((e) => e.type === "combo")).toBe(false);
    const denied = events.find((e) => e.type === "combo_denied");
    expect(denied).toBeDefined();
    expect(denied?.reason).toBe("self_combo");
    expect(boss.health).toBe(1000); // untouched
    // the Window is still open for a different class to close
    expect(boss.effects.some((e) => e.effectId.startsWith("window:"))).toBe(true);
  });
});

describe("Window expiry", () => {
  it("emits window_expired when nobody pays it off in time", () => {
    const warden = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const boss = createActor({ id: 3, kind: "enemy", classId: "boss", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1000, maxResource: 0 });
    const state = stateWith([warden, boss]);
    const rules = makeRules(baseCfg());
    const events: SimEvent[] = [];
    const emit = (e: SimEvent) => events.push(e);

    tick(state, rules, [cast(1, "sundered_strike", 3)], emit);
    for (let i = 0; i < 61; i += 1) tick(state, rules, [], emit);

    const expired = events.find((e) => e.type === "window_expired");
    expect(expired).toBeDefined();
    expect(expired?.windowId).toBe("sundered");
    expect(boss.effects.some((e) => e.effectId.startsWith("window:"))).toBe(false);
  });
});

describe("floor Window field variant", () => {
  it("accepts any other class's payoff standing in the patch for the smaller field combo", () => {
    const warden = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const cinder = createActor({ id: 2, kind: "player", classId: "cinder", role: "damage", pos: { x: 100, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = stateWith([warden, cinder]);
    const rules = makeRules(baseCfg());
    const events: SimEvent[] = [];
    const emit = (e: SimEvent) => events.push(e);

    tick(state, rules, [cast(1, "bulwark", 1)], emit);
    expect(events.some((e) => e.type === "window_opened" && e.onTarget === false)).toBe(true);

    // cinder's payoff targets itself here (shape: target) but resolution falls back to the floor
    // patch since it carries no target-actor Window of its own.
    tick(state, rules, [cast(2, "detonate", 2)], emit);

    const combo = events.find((e) => e.type === "combo");
    expect(combo).toBeDefined();
    expect(combo?.comboId).toBe("shatter-field");
    expect(combo?.field).toBe(true);
    expect(cinder.health).toBe(90); // 100 - 10 field damage (applied to the payoff actor itself)
  });

  it("expires with window_expired if nobody stands in it", () => {
    const warden = createActor({ id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
    const state = stateWith([warden]);
    const rules = makeRules(baseCfg());
    const events: SimEvent[] = [];
    const emit = (e: SimEvent) => events.push(e);

    tick(state, rules, [cast(1, "bulwark", 1)], emit);
    for (let i = 0; i < 61; i += 1) tick(state, rules, [], emit);

    const expired = events.filter((e) => e.type === "window_expired" && e.onTarget === false);
    expect(expired).toHaveLength(1);
  });
});
