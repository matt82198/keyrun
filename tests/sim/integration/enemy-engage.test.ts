/**
 * tests/sim/integration/enemy-engage.test.ts
 *
 * ENEMY-ENGAGE, the TypeScript twin of game/sim/Keyrun.Sim.Tests/Encounter/EnemyEngageTests.cs (Matt 2026-10-20:
 * "enemies need to stack on the player they are attacking as well lmfao, they just face a random direction and swing
 * the air"). A melee enemy with a target closes inside its swing range of THAT target, faces it (ActorState.yaw, the
 * facing frontal cones read and the view draws), and only lands its swing in range. Before the fix,
 * sim/encounter/chase.ts emitted nothing once the target was inside engage - margin, so an enemy spawned in reach kept
 * its spawn yaw and a sidestepping target was never re-faced.
 *
 * Driven through the real public path: client/src/sim-host/run-setup.ts's buildRunSetup (real content, hollow-vault
 * k2: choir.melee, speed 207, choir.meleeSwing range 3000) and sim/index.ts's createRun/stepRun. Only the Choir's
 * Acolyte 100 is spawned; the party never casts, so its target is the nearest player, the tank (actor 1) at the origin;
 * the other four seats are parked 15 m+ behind it.
 *
 * Usage: `npx vitest run tests/sim/integration/enemy-engage.test.ts`
 */
import { describe, expect, it } from "vitest";
import { loadContent } from "@content/index";
import { createRun, stepRun, type Run } from "@sim/index";
import { rangeMm } from "@sim/combat/casting";
import { angleDiff, yawBetween } from "@sim/core/fixed";
import type { Command } from "@sim/core/commands";
import type { ActorState } from "@sim/encounter/types";
import { buildRunSetup } from "../../../client/src/sim-host/run-setup.js";

const SEED = 1;
const TANK_ID = 1;
const ACOLYTE_ID = 100;
const SWING_RANGE_MM = 3000;
/** Facing tolerance in 1024ths of a turn (about 5.6 degrees), the same as the C# twin. */
const YAW_TOLERANCE = 16;
/** 6 m at 207 mm/tick closes inside 3 m in 15 ticks; 40 leaves room for the first swing. */
const CLOSE_WITHIN_TICKS = 40;

interface Tick {
  at: number;
  before: ActorState;
  tankBefore: ActorState;
  after: ActorState;
  tankAfter: ActorState;
  events: Array<Record<string, unknown>>;
}

const clone = (a: ActorState): ActorState => structuredClone(a);

function play(acolyte: { x: number; z: number }, acolyteYaw: number, ticks: number, tankMoves?: Map<number, { x: number; z: number }>): Tick[] {
  const setup = buildRunSetup(loadContent(), { keyLevel: 2, dungeonId: "hollow-vault", humanClassId: "warden" });
  const choir = setup.dungeon.pulls.find((p) => p.id === "choir")!;
  const spawn = choir.spawns.find((s) => s.actorId === ACOLYTE_ID)!;
  expect(spawn.classId).toBe("choir.melee");
  const dungeon = {
    ...setup.dungeon,
    pulls: [{ ...choir, spawns: [{ ...spawn, pos: { x: acolyte.x, y: 0, z: acolyte.z }, yaw: acolyteYaw }] }],
  };
  expect(setup.party.find((m) => m.id === TANK_ID)?.role).toBe("tank");
  const party = setup.party.map((m) =>
    m.id === TANK_ID ? { ...m, pos: { x: 0, y: 0, z: 0 }, yaw: 0 } : { ...m, pos: { x: -15000 - m.id * 1000, y: 0, z: 0 }, yaw: 0 },
  );
  let run: Run = createRun(SEED, party, dungeon, setup.content, setup.runOptions);
  const played: Tick[] = [];
  for (let i = 0; i < ticks; i += 1) {
    const at = run.state.tick;
    const before = clone(run.state.actors[ACOLYTE_ID]!);
    const tankBefore = clone(run.state.actors[TANK_ID]!);
    const commands: Command[] = [];
    const dir = tankMoves?.get(at);
    if (dir) commands.push({ tick: at, actorId: TANK_ID, kind: "move", dir, yaw: 0 });
    const stepped = stepRun(run, commands);
    run = stepped.run;
    played.push({
      at,
      before,
      tankBefore,
      after: clone(run.state.actors[ACOLYTE_ID]!),
      tankAfter: clone(run.state.actors[TANK_ID]!),
      events: stepped.events as unknown as Array<Record<string, unknown>>,
    });
  }
  return played;
}

const facingError = (from: ActorState, to: ActorState): number => Math.abs(angleDiff(from.yaw, yawBetween(from.pos, to.pos)));

function acolyteHits(played: Tick[]): Array<{ t: Tick; hit: Record<string, unknown> }> {
  return played.flatMap((t) => t.events.filter((e) => e.type === "damage" && e.sourceId === ACOLYTE_ID).map((hit) => ({ t, hit })));
}

function worstFacing(ticks: Tick[]): { t: Tick; err: number } {
  return ticks.map((t) => ({ t, err: facingError(t.after, t.tankAfter) })).reduce((w, p) => (p.err > w.err ? p : w));
}

describe("ENEMY-ENGAGE: a melee enemy stacks on and faces the player it attacks", () => {
  it("a melee enemy 6 m from its target closes inside its swing range, faces it and swings", () => {
    const played = play({ x: 6000, z: 0 }, 256, CLOSE_WITHIN_TICKS + 40);
    const end = played[CLOSE_WITHIN_TICKS - 1]!;
    console.log(`t${end.at}: acolyte (${end.after.pos.x},${end.after.pos.z}) yaw ${end.after.yaw}, gap ${rangeMm(end.after, end.tankAfter)} mm, facing error ${facingError(end.after, end.tankAfter)}`);
    expect(rangeMm(end.after, end.tankAfter)).toBeLessThanOrEqual(SWING_RANGE_MM);
    expect(facingError(end.after, end.tankAfter)).toBeLessThanOrEqual(YAW_TOLERANCE);
    expect(acolyteHits(played).length).toBeGreaterThan(0);
  });

  it("a melee enemy already in range of its target turns to face it before it keeps swinging", () => {
    // Spawned 2 m from the tank, inside its stop line, facing directly away (+x, yaw 0; the tank is at -x).
    const played = play({ x: 2000, z: 0 }, 0, 60);
    const worst = worstFacing(played.slice(2));
    console.log(`worst facing error ${worst.err}/1024 at t${worst.t.at}: acolyte yaw ${worst.t.after.yaw}`);
    expect(worst.err).toBeLessThanOrEqual(YAW_TOLERANCE);
    // It is hitting the tank it now faces, from inside its reach.
    expect(acolyteHits(played).length).toBeGreaterThan(0);
  });

  it("a melee enemy keeps facing a target that sidesteps inside its range", () => {
    // The Acolyte closes from 6 m, then the tank steps 8 ticks along +z and +x, staying inside the stop line.
    const moves = new Map<number, { x: number; z: number }>();
    for (let t = 40; t < 48; t += 1) moves.set(t, { x: 1000, z: 1000 });
    const played = play({ x: 6000, z: 0 }, 256, 70, moves);
    const after = played.filter((t) => t.at >= 50);
    for (const t of after) expect(rangeMm(t.after, t.tankAfter), `t${t.at}`).toBeLessThanOrEqual(SWING_RANGE_MM);
    const worst = worstFacing(after);
    console.log(`worst facing error ${worst.err}/1024 at t${worst.t.at}: acolyte yaw ${worst.t.after.yaw}, gap ${rangeMm(worst.t.after, worst.t.tankAfter)}`);
    expect(worst.err).toBeLessThanOrEqual(YAW_TOLERANCE);
  });

  it("every swing that lands is on the tank, from inside swing range, while facing it", () => {
    const played = play({ x: 6000, z: 0 }, 256, 120);
    const hits = acolyteHits(played);
    expect(hits.length).toBeGreaterThan(0);
    for (const { t, hit } of hits) {
      expect(hit.targetId).toBe(TANK_ID);
      expect(rangeMm(t.before, t.tankBefore), `t${t.at}`).toBeLessThanOrEqual(SWING_RANGE_MM);
      expect(facingError(t.after, t.tankAfter), `t${t.at}`).toBeLessThanOrEqual(YAW_TOLERANCE);
    }
  });
});
