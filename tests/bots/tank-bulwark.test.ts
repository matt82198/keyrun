/**
 * tests/bots/tank-bulwark.test.ts
 *
 * Warden tank group mitigation under pack pressure (bots/src/roles/tank-pressure.ts's
 * pressureMitigationCommand), real content kit. Bulwark (a circle that applies Braced, -15%
 * damage taken for 160 ticks, cd 200) is a rotation Setup; kit-priority.ts ranks Payoffs above
 * Setups and Guard Break is a cd-0 Payoff, so the rotation never cast Bulwark. Hollow Vault's
 * pull 1 (five synced Paired Warriors, tests/sim/integration/bots-pull1.test.ts) then sat on a
 * ~10 HP margin and wiped once sim #94 shifted pull 0 by a few ticks.
 *   - 3+ attackers, Brace on cooldown, Vigil unaffordable, Braced down -> Bulwark;
 *   - Braced already up -> no Bulwark (the rotation keeps the GCD);
 *   - two attackers (no pack pressure) -> no Bulwark;
 *   - Bulwark on cooldown -> no Bulwark;
 *   - mid-cast -> no Bulwark (never clip our own cast).
 *
 * Usage: `npx vitest run tests/bots/tank-bulwark.test.ts`
 */
import { describe, expect, it } from "vitest";
import "./real-content-setup.js";
import { tankDecide } from "@bots/src/roles.js";
import type { ActorId, ActorState, BotView, Command, SimState } from "@bots/src/types.js";

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };
const TICK = 10;

/** Brace on cooldown and 0 momentum (Vigil costs 50): the defensives ahead of Bulwark are unavailable. */
function tank(overrides: Partial<ActorState> = {}): ActorState {
  return {
    id: 2, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, yaw: 0,
    health: 400, maxHealth: 400, resource: 0, maxResource: 100, alive: true, targetId: 100,
    castId: null, castEndsTick: null, castStartTick: null, cooldowns: { "warden.brace": 999 }, gcdReadyTick: 0,
    effects: [], threat: {}, ...overrides,
  };
}

/** An enemy in melee range in front of the tank (yaw 0 faces +z), aimed at `targetId`. */
function enemy(id: number, targetId: number): ActorState {
  return {
    ...tank(),
    id, kind: "enemy", classId: "pair.melee", role: "none", pos: { x: 0, y: 0, z: 1500 }, yaw: 512,
    health: 6720, maxHealth: 6720, maxResource: 0, targetId, cooldowns: {}, threat: { 2: 100 },
  };
}

function decide(self: ActorState, others: ActorState[]): Command[] {
  const table: Record<ActorId, ActorState> = {};
  for (const a of [self, ...others]) table[a.id] = a;
  const state: SimState = { tick: TICK, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  const view: BotView = { state, telegraphs: [], classKit: NO_CLASS_KIT };
  return tankDecide(view, self);
}

const BULWARK: Command = { tick: TICK, actorId: 2, kind: "cast", abilityId: "warden.bulwark" };
const pack = (): ActorState[] => [enemy(100, 2), enemy(101, 2), enemy(102, 2)];
const braced = { effectId: "braced", sourceId: 2, stacks: 1, endsTick: TICK + 100, nextPeriodicTick: null };

describe("Warden tank keeps Bulwark up under pack pressure", () => {
  it("casts Bulwark, ahead of the rotation, when three enemies are aimed at it and Braced is down", () => {
    const cmds = decide(tank(), pack());
    expect(cmds).toContainEqual(BULWARK);
    const casts = cmds.filter((c) => c.kind === "cast");
    expect(casts[0]).toEqual(BULWARK);
  });

  it("does not recast Bulwark while its Braced effect is still up", () => {
    expect(decide(tank({ effects: [braced] }), pack())).not.toContainEqual(BULWARK);
  });

  it("does not cast Bulwark with only two attackers (no pack pressure)", () => {
    expect(decide(tank(), [enemy(100, 2), enemy(101, 2)])).not.toContainEqual(BULWARK);
  });

  it("does not cast Bulwark while it is on cooldown", () => {
    const cooling = tank({ cooldowns: { "warden.brace": 999, "warden.bulwark": TICK + 50 } });
    expect(decide(cooling, pack())).not.toContainEqual(BULWARK);
  });

  it("never clips its own cast to start Bulwark", () => {
    const casting = tank({ castId: "warden.bulwark", castStartTick: TICK - 5, castEndsTick: TICK + 15 });
    expect(decide(casting, pack())).not.toContainEqual(BULWARK);
  });
});
