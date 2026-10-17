/**
 * tests/bots/tank-engage.test.ts
 *
 * Warden tank ENGAGE behaviour (bots/src/roles/tank-engage.ts), driven by the real content kit:
 * sim generates threat only from explicit threat ops, so a tank that never taunts or casts its
 * rotation never holds anything (the pull-0 wipe, tests/sim/integration/bots-pull0.test.ts).
 *   - an enemy aimed at another player, in taunt range -> Taunt it;
 *   - the target out of melee reach -> walk toward it;
 *   - in reach -> face the target and cast a rotation ability at it (never Vigil as rotation).
 *
 * Usage: `npx vitest run tests/bots/tank-engage.test.ts`
 */
import { describe, expect, it } from "vitest";
import "./real-content-setup.js";
import { tankDecide } from "@bots/src/roles.js";
import { holdsThreat } from "@bots/src/roles/tank-engage.js";
import type { ActorId, ActorState, BotView, SimState } from "@bots/src/types.js";

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1, kind: "player", classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 0 }, yaw: 0,
    health: 400, maxHealth: 400, resource: 0, maxResource: 100, alive: true, targetId: null,
    castId: null, castEndsTick: null, castStartTick: null, cooldowns: {}, gcdReadyTick: 0,
    effects: [], threat: {}, ...overrides,
  };
}

function makeView(actors: ActorState[]): BotView {
  const table: Record<ActorId, ActorState> = {};
  for (const a of actors) table[a.id] = a;
  const state: SimState = { tick: 10, seed: 1, prng: [1, 2, 3, 4], actors: table, runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs: [], classKit: NO_CLASS_KIT };
}

const ally = makeActor({ id: 3, classId: "cinder", role: "damage", health: 250, maxHealth: 250, pos: { x: 0, y: 0, z: 4000 } });

describe("Warden tank engage", () => {
  it("taunts a loose enemy (aimed at another player) within taunt range", () => {
    const tank = makeActor({ id: 2 });
    const enemy = makeActor({ id: 100, kind: "enemy", classId: "choir.melee", role: "none", pos: { x: 0, y: 0, z: 2000 }, targetId: 3 });
    const cmds = tankDecide(makeView([tank, ally, enemy]), tank);
    expect(cmds).toContainEqual({ tick: 10, actorId: 2, kind: "cast", abilityId: "warden.taunt", targetId: 100 });
  });

  it("walks toward its target when it is beyond melee reach", () => {
    const tank = makeActor({ id: 2, cooldowns: { "warden.taunt": 999 } });
    const enemy = makeActor({ id: 100, kind: "enemy", classId: "choir.melee", role: "none", pos: { x: 0, y: 0, z: 9000 }, targetId: 3 });
    const move = tankDecide(makeView([tank, ally, enemy]), tank).find((c) => c.kind === "move");
    expect(move).toBeDefined();
    if (move && move.kind === "move") expect(move.dir.z).toBeGreaterThan(0);
  });

  it("in reach of a held enemy: faces it and casts a rotation ability at it (never Vigil)", () => {
    const tank = makeActor({ id: 2, yaw: 768, resource: 60, targetId: 100 });
    const enemy = makeActor({ id: 100, kind: "enemy", classId: "choir.melee", role: "none", pos: { x: 0, y: 0, z: 2000 }, targetId: 2, threat: { 2: 10 } });
    const cmds = tankDecide(makeView([tank, ally, enemy]), tank);
    const face = cmds.find((c) => c.kind === "move");
    expect(face && face.kind === "move" ? face.yaw : null).toBe(256); // +z, toward the enemy
    const cast = cmds.find((c) => c.kind === "cast");
    expect(cast).toBeDefined();
    if (cast && cast.kind === "cast") {
      expect(["warden.strike", "warden.guardBreak", "warden.bulwark", "warden.reckoning"]).toContain(cast.abilityId);
      expect(cast.targetId).toBe(100);
    }
  });

  it("holdsThreat: needs an entry at least as high as every other one", () => {
    expect(holdsThreat(makeActor({ threat: {} }), 2)).toBe(false);
    expect(holdsThreat(makeActor({ threat: { 2: 0 } }), 2)).toBe(true);
    expect(holdsThreat(makeActor({ threat: { 2: 10, 3: 50 } }), 2)).toBe(false);
  });
});
