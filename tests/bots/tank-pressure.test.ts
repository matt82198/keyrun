/**
 * tests/bots/tank-pressure.test.ts
 *
 * Warden tank pack-pressure defensive (bots/src/roles/tank.ts's PRESSURE_ATTACKERS), real content
 * kit. Hollow Vault's pull 1 (five Paired Warriors, tests/sim/integration/bots-pull1.test.ts)
 * swings in sync: ~350 damage lands in one tick on a 400-HP Warden, so a Brace gated only on the
 * 35% health floor was never cast before the tank died.
 *   - 3+ enemies aimed at the tank, full health -> Brace;
 *   - 2 enemies aimed at the tank, full health -> no Brace (the floor still governs);
 *   - low health with a single attacker -> Brace (the old floor is kept).
 *
 * Usage: `npx vitest run tests/bots/tank-pressure.test.ts`
 */
import { describe, expect, it } from "vitest";
import "./real-content-setup.js";
import { tankDecide } from "@bots/src/roles.js";
import type { ActorId, ActorState, BotView, Command, SimState } from "@bots/src/types.js";

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 2, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, yaw: 256,
    health: 400, maxHealth: 400, resource: 0, maxResource: 100, alive: true, targetId: 100,
    castId: null, castEndsTick: null, castStartTick: null, cooldowns: {}, gcdReadyTick: 0,
    effects: [], threat: {}, ...overrides,
  };
}

function enemy(id: number, targetId: number): ActorState {
  return makeActor({ id, kind: "enemy", classId: "pair.melee", role: "none", pos: { x: 0, y: 0, z: 2000 }, targetId, threat: { 2: 0 } });
}

function decide(tank: ActorState, others: ActorState[]): Command[] {
  const table: Record<ActorId, ActorState> = {};
  for (const a of [tank, ...others]) table[a.id] = a;
  const state: SimState = { tick: 10, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  const view: BotView = { state, telegraphs: [], classKit: NO_CLASS_KIT };
  return tankDecide(view, tank);
}

const BRACE: Command = { tick: 10, actorId: 2, kind: "cast", abilityId: "warden.brace" };

describe("Warden tank pack pressure", () => {
  it("braces at full health once three enemies are aimed at it", () => {
    const cmds = decide(makeActor({}), [enemy(100, 2), enemy(101, 2), enemy(102, 2)]);
    expect(cmds).toContainEqual(BRACE);
  });

  it("does not brace at full health with only two attackers", () => {
    const cmds = decide(makeActor({}), [enemy(100, 2), enemy(101, 2)]);
    expect(cmds).not.toContainEqual(BRACE);
  });

  it("counts only enemies aimed at the tank (three enemies, one on the tank -> no brace)", () => {
    const healer = makeActor({ id: 3, classId: "chanter", role: "healer", pos: { x: 0, y: 0, z: -4000 }, targetId: null });
    const cmds = decide(makeActor({}), [healer, enemy(100, 2), enemy(101, 3), enemy(102, 3)]);
    expect(cmds).not.toContainEqual(BRACE);
  });

  it("still braces under the 35% health floor with a single attacker", () => {
    const cmds = decide(makeActor({ health: 100 }), [enemy(100, 2)]);
    expect(cmds).toContainEqual(BRACE);
  });
});
