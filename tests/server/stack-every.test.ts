// Usage: scale-step-keeps-every -- server/src/content-setup.ts's own `scaleStep` (a port of
// client/src/sim-host/content-dungeon.ts's) must keep a numeric-`at` step's `every`, Tempo-scaled
// like a "loop" step's. Without it pair.stackCall (`at: 600, every: 400`) fires once at global tick
// 600 (pull 1) and never during The Pair. Drives the real `buildServerContent` output -- the exact
// `RunContent.enemyScripts` server/src/room.ts hands to createRun -- through sim's real
// stepEnemyScripts over a pull-2 window.
import { describe, expect, it } from "vitest";
import { buildServerContent, DUNGEON_ID, loadServerRegistry } from "../../server/src/content-setup.js";
import { createState } from "../../sim/core/state.js";
import { spawnPull } from "../../sim/encounter/pulls.js";
import { stepEnemyScripts } from "../../sim/encounter/scripts.js";
import { scaleCastEvery, scaleLeadTicks } from "../../sim/encounter/tempo.js";

const KEY = 2;
const PULL2_START_TICK = 1500;
const WINDOW_TICKS = 1200;

function pairSetup() {
  const server = buildServerContent(loadServerRegistry(), DUNGEON_ID, KEY);
  const pairIndex = server.dungeon.pulls.findIndex((p) => p.spawns.some((s) => s.classId === "pair.melee"));
  const pairId = server.dungeon.pulls[pairIndex]!.spawns.find((s) => s.classId === "pair.melee")!.actorId;
  const step = server.content.enemyScripts[pairId]!.find((s) => s.cast === "pair.stackCall")!;
  return { server, pairIndex, step };
}

describe("server: phased recurring steps keep `every`", () => {
  it("scales pair.stackCall's `every` with scaleCastEvery and leaves `at` as the phase offset", () => {
    const { step } = pairSetup();
    expect(step.at).toBe(600);
    expect(step.every).toBe(scaleCastEvery(400, KEY));
    expect(step.every).not.toBe(400);
    expect(step.telegraph!.leadTicks).toBe(scaleLeadTicks(100, KEY));
  });

  it("a scripted pull-2 run at key 2 emits >=2 STACK telegraphs during The Pair", () => {
    const { server, pairIndex } = pairSetup();
    const state = createState(1);
    state.tick = PULL2_START_TICK;
    for (const actor of spawnPull(server.dungeon, pairIndex, PULL2_START_TICK, () => {})) state.actors[actor.id] = actor;

    const stackResolves = new Set<number>();
    for (let t = PULL2_START_TICK; t < PULL2_START_TICK + WINDOW_TICKS; t++) {
      state.tick = t;
      const { telegraphEvents } = stepEnemyScripts(state, server.content.enemyScripts);
      for (const e of telegraphEvents) {
        if (e.type === "telegraph" && e.cast === "pair.stackCall" && typeof e.resolvesTick === "number") stackResolves.add(e.resolvesTick);
      }
    }
    expect(stackResolves.size).toBeGreaterThanOrEqual(2);
  });
});
