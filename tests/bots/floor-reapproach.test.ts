/**
 * tests/bots/floor-reapproach.test.ts
 *
 * bots/src/shared/floor.ts's two follow-on behaviours once a floor-patch escape has moved a bot:
 * a melee bot (bots/CLAUDE.md's dispatch brief: Warden, plus any tank) walks back to its target
 * once no longer clearing a hostile patch, and a healer's floor escape prefers an escape point that
 * keeps it within heal range of the party (bots/CLAUDE.md: "stay in range") rather than the plain
 * nearest safe ground.
 *
 * Usage: `npx vitest run tests/bots/floor-reapproach.test.ts`
 */
import { describe, expect, it } from "vitest";
import { damageDecide, healerDecide } from "@bots/src/roles.js";
import { floorEscapeCommand, FLOOR_PATCH_PREFIX } from "@bots/src/shared/floor.js";
import { isInAnyDanger, toMM } from "@bots/src/shared/positioning.js";
import type { ActorId, ActorState, BotView, SimState, Telegraph } from "@bots/src/types.js";

const NO_CLASS_KIT = { rotation: [], interruptAbilityId: null, defensiveAbilityId: null, healAbilityId: null };
/** Mirrors roles/healer.ts's own HEAL_RANGE_M -- kept local since that constant isn't exported. */
const HEAL_RANGE_M = 30;

function makeActor(overrides: Partial<ActorState>): ActorState {
  return {
    id: 1,
    kind: "player",
    classId: "warden",
    role: "damage",
    pos: { x: 0, y: 0, z: 0 },
    yaw: 0,
    health: 100,
    maxHealth: 100,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId: null,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
    ...overrides,
  };
}

function table(actors: ActorState[]): Record<ActorId, ActorState> {
  const out: Record<ActorId, ActorState> = {};
  for (const a of actors) out[a.id] = a;
  return out;
}

function makeView(actors: ActorState[], telegraphs: Telegraph[] = [], tick = 10): BotView {
  const state: SimState = { tick, seed: 1, prng: [1, 2, 3, 4], actors: table(actors), runTimerTicks: 0, phase: "combat", log: [] };
  return { state, telegraphs, classKit: NO_CLASS_KIT };
}

describe("melee re-approach", () => {
  it("flees the floor patch instead of chasing its target while still standing in it", () => {
    const target = makeActor({ id: 100, kind: "enemy", pos: { x: 0, y: 0, z: 0 } });
    const patch: Telegraph = {
      id: `${FLOOR_PATCH_PREFIX}fire`,
      shape: "circle",
      center: { x: 0, z: 2 },
      radius: 3,
      resolvesTick: 1000,
      danger: false,
    };
    const withTarget = makeActor({ id: 1, classId: "warden", pos: { x: 0, y: 0, z: 2 }, targetId: 100 });
    const view = makeView([withTarget, target], [patch]);

    const commands = damageDecide(view, withTarget);
    const escape = floorEscapeCommand(view, withTarget);
    expect(escape).not.toBeNull();
    expect(commands).toEqual([escape]);
  });

  it("re-approaches its target once no longer clearing any floor patch", () => {
    const target = makeActor({ id: 100, kind: "enemy", pos: { x: 0, y: 0, z: 0 } });
    const self = makeActor({ id: 1, classId: "warden", pos: { x: 0, y: 0, z: 10 }, targetId: 100 });
    const view = makeView([self, target]); // no floor patches: nothing left to escape

    const commands = damageDecide(view, self);
    const move = commands.find((c) => c.kind === "move");
    expect(move).toBeDefined();
    if (move && move.kind === "move") {
      // Target is due south (z=0) of the bot (z=10): walking back in means dz < 0.
      expect(move.dir.z).toBeLessThan(0);
    }
  });

  it("a ranged class does not chase its target (only tank/warden are treated as melee)", () => {
    const target = makeActor({ id: 100, kind: "enemy", pos: { x: 0, y: 0, z: 0 } });
    const self = makeActor({ id: 1, classId: "cinder", role: "damage", pos: { x: 0, y: 0, z: 10 }, targetId: 100 });
    const view = makeView([self, target]);

    expect(damageDecide(view, self)).toEqual([]);
  });
});

describe("healer keeps range while escaping a floor patch", () => {
  it("ends up within heal range of the party after clearing a patch centred on it", () => {
    const STEP_METRES = 1;
    const MAX_TICKS = 40;
    const patch: Telegraph = {
      id: `${FLOOR_PATCH_PREFIX}fire`,
      shape: "circle",
      center: { x: -29, z: 0 },
      radius: 2,
      resolvesTick: 1000,
      danger: false,
    };
    let pos = { x: -29, z: 0 };
    let ticks = 0;
    for (; ticks < MAX_TICKS; ticks++) {
      const posMM = { x: toMM(pos.x), z: toMM(pos.z) };
      if (!isInAnyDanger(posMM, [{ ...patch, radius: 2.5 }])) break;
      const healer = makeActor({ id: 1, classId: "chanter", role: "healer", pos: { x: pos.x, y: 0, z: pos.z } });
      const ally = makeActor({ id: 2, classId: "warden", role: "damage", pos: { x: 0, y: 0, z: 0 } });
      const view = makeView([healer, ally], [patch], ticks);

      const move = healerDecide(view, healer).find((c) => c.kind === "move");
      expect(move).toBeDefined();
      if (move && move.kind === "move") {
        pos = { x: pos.x + move.dir.x * STEP_METRES, z: pos.z + move.dir.z * STEP_METRES };
      }
    }

    expect(ticks).toBeLessThan(MAX_TICKS);
    const distToAlly = Math.hypot(pos.x, pos.z);
    expect(distToAlly).toBeLessThanOrEqual(HEAL_RANGE_M + 2); // small slack for discrete step overshoot
  });
});
