/**
 * tests/sim/encounter/telegraph-facing.test.ts
 *
 * One facing convention across the sim: a boss that faces a target via the combat/targeting
 * convention (sim/core/fixed.ts yawBetween, sim/combat/targeting.ts faceDir: yaw 0 = +x) must have
 * its cone and line telegraphs (sim/encounter/scripts.ts copies actor.yaw into the telegraph event;
 * sim/encounter/telegraphs.ts resolves it) cover the point directly IN FRONT of it and exclude a
 * point to its SIDE. Before the fix the resolver read yaw 0 as +z, so a Cleave resolved 90 degrees
 * off the boss's real facing.
 */
import { describe, expect, it } from "vitest";
import { stepEnemyScripts, type EnemyScript } from "../../../sim/encounter/scripts";
import { pointInCone, pointInLine } from "../../../sim/encounter/telegraphs";
import { faceDir, inCone } from "../../../sim/combat/targeting";
import { yawBetween } from "../../../sim/core/fixed";
import type { ActorState, SimState, Vec3 } from "../../../sim/encounter/types";

function actor(id: number, kind: "enemy" | "player", pos: Vec3, yaw: number): ActorState {
  return {
    id,
    kind,
    classId: kind === "enemy" ? "boss" : "warden",
    role: kind === "enemy" ? "none" : "tank",
    pos,
    yaw,
    health: 1000,
    maxHealth: 1000,
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
  };
}

const BOSS_POS: Vec3 = { x: 2000, y: 0, z: -1000 };
// Target offsets that are not axis-symmetric, so a 90-degree convention swap cannot pass by accident.
const TARGET_OFFSETS: Array<{ x: number; z: number }> = [
  { x: 4000, z: 0 },
  { x: 0, z: 4000 },
  { x: 3000, z: 1000 },
  { x: -2000, z: 3000 },
  { x: -3000, z: -2500 },
];

/** Emits a telegraph through the real script boundary for a boss facing `target`. */
function telegraphFor(shape: "cone" | "line", target: Vec3): { yaw: number; origin: Vec3 } {
  const yaw = yawBetween(BOSS_POS, target);
  const boss = actor(1, "enemy", BOSS_POS, yaw);
  // The telegraph event is emitted `leadTicks` (40) before the tick-100 loop cast.
  const state: SimState = {
    tick: 60,
    seed: 1,
    prng: [1, 2, 3, 4],
    actors: { 1: boss },
    runTimerTicks: 60,
    phase: "combat",
    log: [],
  };
  const tgSpec =
    shape === "cone"
      ? { shape, angle1024: 170, radius: 6000, leadTicks: 40 }
      : { shape, length: 8000, width: 1000, leadTicks: 40 };
  const script: EnemyScript = [{ at: "loop", every: 100, cast: "boss_cleave", telegraph: tgSpec }];
  const [event] = stepEnemyScripts(state, { 1: script }).telegraphEvents;
  expect(event).toBeDefined();
  return { yaw: event!.yaw as number, origin: event!.origin as Vec3 };
}

/** Points 3 m directly in front of / to the side of the boss, using the combat faceDir vector. */
function frontAndSide(yaw: number): { front: Vec3; side: Vec3 } {
  const f = faceDir(yaw); // Q12 (4096) scaled
  const d = 3000;
  const front = { x: BOSS_POS.x + Math.round((f.x * d) / 4096), y: 0, z: BOSS_POS.z + Math.round((f.z * d) / 4096) };
  const side = { x: BOSS_POS.x + Math.round((-f.z * d) / 4096), y: 0, z: BOSS_POS.z + Math.round((f.x * d) / 4096) };
  return { front, side };
}

describe("telegraph facing matches combat facing", () => {
  for (const off of TARGET_OFFSETS) {
    const target = { x: BOSS_POS.x + off.x, y: 0, z: BOSS_POS.z + off.z };
    it(`cone toward offset (${off.x}, ${off.z}) covers the front and spares the side`, () => {
      const { yaw, origin } = telegraphFor("cone", target);
      const { front, side } = frontAndSide(yaw);
      // Sanity: the combat resolver agrees the front point is in front.
      expect(inCone(actor(1, "enemy", BOSS_POS, yaw), 170, 6000, [actor(2, "player", front, 0)])).toEqual([2]);
      expect(pointInCone(origin, yaw, 170, 6000, front)).toBe(true);
      expect(pointInCone(origin, yaw, 170, 6000, side)).toBe(false);
      expect(pointInCone(origin, yaw, 170, 6000, target)).toBe(true);
    });
    it(`line toward offset (${off.x}, ${off.z}) covers the front and spares the side`, () => {
      const { yaw, origin } = telegraphFor("line", target);
      const { front, side } = frontAndSide(yaw);
      expect(pointInLine(origin, yaw, 8000, 1000, front)).toBe(true);
      expect(pointInLine(origin, yaw, 8000, 1000, side)).toBe(false);
      expect(pointInLine(origin, yaw, 8000, 1000, target)).toBe(true);
    });
  }
});
