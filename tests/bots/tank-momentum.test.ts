/**
 * tests/bots/tank-momentum.test.ts
 *
 * Warden tank pressure-aware momentum spend (bots/src/roles/tank-pressure.ts), real content kit.
 * Hollow Vault's pull 1 (five synced Paired Warriors, tests/sim/integration/bots-pull1.test.ts):
 * the tank sat at 310/400 with 100 momentum and dumped it into Reckoning (kit-priority.ts puts
 * cost-paying finishers first), so once Brace's 60 ticks ran out nothing mitigated the ~350
 * one-tick burst and it died. Under pack pressure the tank now holds momentum for Vigil and casts
 * it when Brace is down or about to expire; with no pressure Reckoning stays the finisher.
 *
 * Usage: `npx vitest run tests/bots/tank-momentum.test.ts`
 */
import { describe, expect, it } from "vitest";
import "./real-content-setup.js";
import { tankDecide } from "@bots/src/roles.js";
import type { ActorId, ActorState, BotView, Command, EffectInstance, SimState } from "@bots/src/types.js";

const TICK = 1000;
const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };
/** Brace cast 55 ticks ago: on cooldown, its 60-tick braceSelf effect has 5 ticks left. */
const BRACE_ON_CD = { "warden.brace": TICK + 245 };

function makeTank(overrides: Partial<ActorState>): ActorState {
  return {
    id: 2, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, yaw: 0,
    health: 310, maxHealth: 400, resource: 100, maxResource: 100, alive: true, targetId: 100,
    castId: null, castEndsTick: null, castStartTick: null, cooldowns: { ...BRACE_ON_CD }, gcdReadyTick: 0,
    effects: [], threat: {}, ...overrides,
  };
}

function effect(effectId: string, endsTick: number): EffectInstance {
  return { effectId, sourceId: 2, stacks: 1, endsTick, nextPeriodicTick: null };
}

function enemy(id: number): ActorState {
  return makeTank({
    id, kind: "enemy", classId: "pair.melee", role: "none", health: 6720, maxHealth: 6720, resource: 0,
    pos: { x: 0, y: 0, z: 2000 }, targetId: 2, threat: { 2: 50 }, cooldowns: {},
  });
}

function casts(tank: ActorState, enemies: number): string[] {
  const table: Record<ActorId, ActorState> = { [tank.id]: tank };
  for (let i = 0; i < enemies; i++) table[100 + i] = enemy(100 + i);
  const state: SimState = { tick: TICK, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  const view: BotView = { state, telegraphs: [], classKit: NO_CLASS_KIT };
  return tankDecide(view, tank)
    .filter((c): c is Extract<Command, { kind: "cast" }> => c.kind === "cast")
    .map((c) => c.abilityId);
}

describe("Warden tank momentum under pack pressure", () => {
  it("pressured, 100 momentum, Brace about to expire -> casts Vigil, not Reckoning", () => {
    const ids = casts(makeTank({ effects: [effect("braceSelf", TICK + 5)] }), 5);
    expect(ids).toContain("warden.vigil");
    expect(ids).not.toContain("warden.reckoning");
  });

  it("pressured, 100 momentum, Brace down -> casts Vigil, not Reckoning", () => {
    const ids = casts(makeTank({}), 3);
    expect(ids).toContain("warden.vigil");
    expect(ids).not.toContain("warden.reckoning");
  });

  it("pressured with Brace fresh -> holds momentum: neither Reckoning nor Vigil yet", () => {
    const ids = casts(makeTank({ effects: [effect("braceSelf", TICK + 50)] }), 5);
    expect(ids).not.toContain("warden.reckoning");
    expect(ids).not.toContain("warden.vigil");
    expect(ids.length).toBeGreaterThan(0); // still swings a momentum builder
  });

  it("pressured with Vigil on cooldown -> still holds momentum (no Reckoning)", () => {
    const ids = casts(makeTank({ cooldowns: { ...BRACE_ON_CD, "warden.vigil": TICK + 60 }, effects: [effect("bracedSelf", TICK + 40)] }), 5);
    expect(ids).not.toContain("warden.reckoning");
  });

  it("unpressured (one attacker), 100 momentum, 310/400 HP -> Reckoning stays the finisher", () => {
    const ids = casts(makeTank({}), 1);
    expect(ids).toContain("warden.reckoning");
    expect(ids).not.toContain("warden.vigil");
  });
});
