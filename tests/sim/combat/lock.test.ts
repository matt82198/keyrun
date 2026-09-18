/**
 * The Lock (design/BIBLE.md §9): a boss ability opens a Window on itself
 * ({ op: "window", self: true, alternate: true }) that only a Payoff from a class other than
 * whoever closed its last Lock may close (alternation), and which enrages the boss after three
 * unpaid (expired) Locks. Also covers determinism of the whole Windows-and-Combos event/state
 * sequence, per sim/CLAUDE.md's "same seed + commands -> same hash" invariant — sim/combat/types.ts
 * (this lane's standalone type copy) carries no hashState of its own, so this test uses a local
 * canonical-JSON + FNV-1a hash of the same shape sim/core/state.ts documents, rather than
 * importing sim/core (out of this lane's scope).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import type { AbilityDef } from "../../../sim/combat/abilities";
import { makeRules, type RulesConfig } from "../../../sim/combat/rules";
import type { Command, SimEvent, SimState } from "../../../sim/combat/types";
import type { ComboTable } from "../../../sim/combat/combos";

const lockAbility: AbilityDef = {
  id: "the_lock",
  castTicks: 0,
  gcdTicks: 0,
  cooldownTicks: 0,
  charges: 1,
  cost: { resource: "energy", amount: 0 },
  range: 0,
  shape: { kind: "self" },
  interruptible: false,
  effects: [{ op: "window", windowId: "lock", durationTicks: 60, onTarget: true, setupClass: "boss", self: true, alternate: true }],
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

const combos: ComboTable = {
  boss: {
    warden: { id: "lock-break", effects: [{ op: "damage", amount: 25, school: "physical" }] },
    cinder: { id: "lock-break", effects: [{ op: "damage", amount: 25, school: "physical" }] },
    chanter: { id: "lock-break", effects: [{ op: "damage", amount: 25, school: "physical" }] },
  },
};

function cfg(): RulesConfig {
  return {
    abilities: { the_lock: lockAbility, detonate: payoffAbility },
    effects: {},
    lockoutTicks: 60,
    baseSpeedMmPerTick: 300,
    actorRadiusMm: 50,
    bounds: { minX: -10000, maxX: 10000, minZ: -10000, maxZ: 10000 },
    combos,
    fieldCombos: {},
  };
}

function freshState(): { state: SimState; boss: ReturnType<typeof createActor>; warden: ReturnType<typeof createActor>; cinder: ReturnType<typeof createActor> } {
  const boss = createActor({ id: 1, kind: "enemy", classId: "boss", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 10000, maxResource: 0 });
  const warden = createActor({ id: 2, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
  const cinder = createActor({ id: 3, kind: "player", classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
  const record: SimState["actors"] = { 1: boss, 2: warden, 3: cinder };
  const state: SimState = { tick: 0, seed: 7, prng: [1, 2, 3, 4], actors: record, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, boss, warden, cinder };
}

function cast(actorId: number, abilityId: string, targetId?: number): Command {
  return targetId === undefined
    ? { tick: 0, actorId, kind: "cast", abilityId }
    : { tick: 0, actorId, kind: "cast", abilityId, targetId };
}

// rules() itself never advances state.tick -- sim/core/tick.ts's step() is the single owner of the
// tick (per sim/CLAUDE.md). This suite drives rules() directly (not through step()), so it must
// advance state.tick itself between calls, exactly as step() does.
function tick(state: SimState, rules: ReturnType<typeof makeRules>, cmds: Command[], emit: (e: SimEvent) => void): void {
  rules(state, cmds, emit);
  state.tick += 1;
}

describe("Lock alternation across two consecutive Locks", () => {
  it("lets warden close the first Lock, then denies warden and requires cinder on the second", () => {
    const { state, boss } = freshState();
    const rules = makeRules(cfg());
    const events: SimEvent[] = [];
    const emit = (e: SimEvent) => events.push(e);

    tick(state, rules, [cast(1, "the_lock")], emit); // first Lock opens
    tick(state, rules, [cast(2, "detonate", 1)], emit); // warden closes it

    const firstCombo = events.find((e) => e.type === "combo");
    expect(firstCombo?.payoffActorId).toBe(2);
    expect(boss.health).toBe(9975);

    tick(state, rules, [cast(1, "the_lock")], emit); // second Lock opens
    tick(state, rules, [cast(2, "detonate", 1)], emit); // warden tries again -> denied

    const denied = events.filter((e) => e.type === "combo_denied");
    expect(denied).toHaveLength(1);
    expect(denied[0]?.reason).toBe("lock_alternate");
    expect(boss.health).toBe(9975); // unchanged, denial did no damage

    tick(state, rules, [cast(3, "detonate", 1)], emit); // cinder closes the second Lock

    const combos = events.filter((e) => e.type === "combo");
    expect(combos).toHaveLength(2);
    expect(combos[1]?.payoffActorId).toBe(3);
    expect(boss.health).toBe(9950);
  });
});

describe("three failed Locks", () => {
  it("emits lock_enrage on the third unpaid expiry, and not before", () => {
    const { state, boss } = freshState();
    const rules = makeRules(cfg());
    const events: SimEvent[] = [];
    const emit = (e: SimEvent) => events.push(e);

    for (let cycle = 0; cycle < 3; cycle += 1) {
      tick(state, rules, [cast(1, "the_lock")], emit);
      for (let i = 0; i < 61; i += 1) tick(state, rules, [], emit);
    }

    const enrages = events.filter((e) => e.type === "lock_enrage");
    expect(enrages).toHaveLength(1);
    expect(enrages[0]?.actorId).toBe(boss.id);

    const expiredLocks = events.filter((e) => e.type === "window_expired" && e.lock === true);
    expect(expiredLocks).toHaveLength(3);
    // lock_enrage must land no earlier than the third expiry
    expect(events.indexOf(enrages[0]!)).toBeGreaterThan(events.indexOf(expiredLocks[1]!));
  });
});

// --- determinism -----------------------------------------------------------------------------

function canonicalStringify(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonicalStringify).join(",")}]`;
  if (value !== null && typeof value === "object") {
    const keys = Object.keys(value as Record<string, unknown>).sort();
    const body = keys.map((k) => `${JSON.stringify(k)}:${canonicalStringify((value as Record<string, unknown>)[k])}`);
    return `{${body.join(",")}}`;
  }
  return JSON.stringify(value);
}

function fnv1a(str: string): string {
  let hash = 0x811c9dc5;
  for (let i = 0; i < str.length; i += 1) {
    hash ^= str.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash.toString(16).padStart(8, "0");
}

/** Same technique sim/core/state.ts's hashState documents (canonical-JSON + FNV-1a), applied
 * locally since sim/combat/types.ts carries no hashState of its own (see file docstring). */
function localHashState(s: SimState): string {
  return fnv1a(canonicalStringify(s));
}

function runFullSequence(): { finalHash: string; eventTypes: string[] } {
  const { state } = freshState();
  const rules = makeRules(cfg());
  const events: SimEvent[] = [];
  const emit = (e: SimEvent) => events.push(e);

  tick(state, rules, [cast(1, "the_lock")], emit); // Lock #1 opens
  tick(state, rules, [cast(2, "detonate", 1)], emit); // warden closes it
  tick(state, rules, [cast(1, "the_lock")], emit); // Lock #2 opens
  tick(state, rules, [cast(2, "detonate", 1)], emit); // warden denied (alternation)
  tick(state, rules, [cast(3, "detonate", 1)], emit); // cinder closes it
  tick(state, rules, [cast(1, "the_lock")], emit); // Lock #3 opens, left unpaid
  for (let i = 0; i < 61; i += 1) tick(state, rules, [], emit); // ...and expires unpaid

  return { finalHash: localHashState(state), eventTypes: events.map((e) => e.type) };
}

describe("determinism of the Windows-and-Combos sequence", () => {
  it("produces an identical final state hash and event sequence across independent runs", () => {
    const runs = [runFullSequence(), runFullSequence(), runFullSequence()];
    expect(runs[1]!.finalHash).toBe(runs[0]!.finalHash);
    expect(runs[2]!.finalHash).toBe(runs[0]!.finalHash);
    expect(runs[1]!.eventTypes).toEqual(runs[0]!.eventTypes);
    expect(runs[2]!.eventTypes).toEqual(runs[0]!.eventTypes);
  });
});
