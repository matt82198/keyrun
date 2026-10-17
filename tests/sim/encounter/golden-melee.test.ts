/**
 * tests/sim/encounter/golden-melee.test.ts
 *
 * Refutation guard for STATE.md defect (c) "Golden never lands a melee hit". Drives the REAL
 * content-pull golden scenario (tests/sim/golden/build-content-pull.ts: real content/ registry,
 * sim/index.ts's createRun/stepRun, Hollow Vault's Choir pull) and asserts the Choir Acolytes'
 * scripted `choir.meleeSwing` actually lands on a player: every swing that completes is paired with
 * a same-tick damage event from that enemy onto a player, and the first one lands on the swing
 * loop's first scheduled tick (40, `every: 40` in content/data/enemies/choir.json), so no early
 * swing is silently range-gated away.
 *
 * Why the defect was filed: build-content-pull.ts's WHY note (copied into content-pull.json) still
 * says enemy swings "never execute today" -- that predates the enemy-targeting and #60 chase fixes
 * and is stale; the scenario lands enemy melee hits from tick 40 onward.
 *
 * Usage: `npx vitest run tests/sim/encounter/golden-melee.test.ts`
 */
import { describe, expect, it } from "vitest";
import type { SimEvent } from "@sim/core/events";
import { ENEMY_1, ENEMY_2, runScenario } from "../golden/build-content-pull";

const MELEE_ENEMIES = new Set<number>([ENEMY_1, ENEMY_2]);
const FIRST_SWING_TICK = 40;

interface Hit {
  tick: number;
  sourceId: number;
  targetId: number;
  healthDamage: number;
}

function meleeSwingsAndHits(events: SimEvent[], playerIds: Set<number>): { swings: number; hits: Hit[] } {
  let swings = 0;
  const hits: Hit[] = [];
  for (const e of events) {
    if (e.type === "cast_complete" && MELEE_ENEMIES.has(e.actorId as number) && e.abilityId === "choir.meleeSwing") {
      swings += 1;
    }
    if (e.type === "damage" && MELEE_ENEMIES.has(e.sourceId as number) && playerIds.has(e.targetId as number)) {
      hits.push({ tick: e.tick, sourceId: e.sourceId as number, targetId: e.targetId as number, healthDamage: e.healthDamage as number });
    }
  }
  return { swings, hits };
}

describe("content-pull golden: Choir melee lands on the party (defect (c) refutation)", () => {
  it("every completed enemy melee swing deals health damage to a player, starting on the first scheduled swing", () => {
    const { run, events } = runScenario();
    const playerIds = new Set(Object.values(run.state.actors).filter((a) => a.kind === "player").map((a) => a.id));
    expect(playerIds.size).toBeGreaterThan(0);
    const { swings, hits } = meleeSwingsAndHits(events, playerIds);

    expect(swings).toBeGreaterThan(0);
    expect(hits.length).toBe(swings);
    expect(hits.every((h) => h.healthDamage > 0)).toBe(true);
    expect(Math.min(...hits.map((h) => h.tick))).toBe(FIRST_SWING_TICK);
    // Both acolytes swing, not just one.
    expect(new Set(hits.map((h) => h.sourceId))).toEqual(MELEE_ENEMIES);
  });
});
