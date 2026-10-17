/**
 * tests/sim/integration/chase-speed.test.ts
 *
 * Public-path repro for defect (d) "chasers move at the players' speed": a lone player fleeing
 * straight away from Hollow Vault's Choir at base speed, driven through the real content/ registry
 * (loadContent/toSimDungeon) and sim/index.ts's createRun/stepRun. Before per-enemy speed, every
 * `move` used RulesConfig.baseSpeedMmPerTick, so enemy 1000 held a constant ~3673mm gap for 250
 * ticks and landed 0 hits. With content's choir.melee `speedMmPerTick` (provisional 1.15x base,
 * [DECIDE Matt] in content/data/TUNING.md) the chaser closes and swings within the same budget.
 *
 * Usage: `npx vitest run tests/sim/integration/chase-speed.test.ts`
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type RunContent } from "@sim/index";
import type { Command } from "@sim/core/commands";
import type { SimEvent } from "@sim/core/events";
import type { DungeonDef } from "@sim/encounter/index";
import { rangeMm } from "@sim/combat/casting";
import { loadContent, toSimDungeon } from "@content/index";

const PLAYER = 1;
const ENEMY = 1000;
const BASE_SPEED = 180;
const TICKS = 250;

function fleeRun(): { hits: number; firstGap: number; lastGap: number } {
  const reg = loadContent();
  const expanded = toSimDungeon(reg.dungeons.get("hollow-vault")!, reg.enemies, 1000);
  const dungeon: DungeonDef = { id: expanded.dungeon.id, pulls: [expanded.dungeon.pulls[0]!] };
  const content: RunContent = {
    abilities: Object.fromEntries(reg.abilities),
    effects: Object.fromEntries(reg.effects),
    lockoutTicks: 80,
    baseSpeedMmPerTick: BASE_SPEED,
    actorRadiusMm: 400,
    // Wide enough that the fleeing player (250 * 180mm) never hits a wall and stops.
    bounds: { minX: -100000, maxX: 100000, minZ: -100000, maxZ: 100000 },
    enemyScripts: expanded.enemyScripts,
  };
  const party = [{ id: PLAYER, classId: "warden", role: "tank" as const, pos: { x: 0, y: 0, z: 4700 }, maxHealth: 2000, maxResource: 150 }];
  let run = createRun(7, party, dungeon, content);
  const firstGap = rangeMm(run.state.actors[ENEMY]!, run.state.actors[PLAYER]!);
  const events: SimEvent[] = [];
  for (let i = 0; i < TICKS; i += 1) {
    const flee: Command = { tick: run.state.tick, actorId: PLAYER, kind: "move", dir: { x: 0, z: 1000 }, yaw: 0 };
    const out = stepRun(run, [flee]);
    run = out.run;
    events.push(...out.events);
  }
  const hits = events.filter((e) => e.type === "damage" && e.sourceId === ENEMY && e.targetId === PLAYER).length;
  return { hits, firstGap, lastGap: rangeMm(run.state.actors[ENEMY]!, run.state.actors[PLAYER]!) };
}

describe("per-enemy chase speed (defect (d))", () => {
  it("a melee chaser faster than base speed catches a player fleeing at base speed within 250 ticks", () => {
    const { hits, firstGap, lastGap } = fleeRun();
    expect(firstGap).toBeGreaterThan(3000); // starts outside choir.meleeSwing's 3000mm range
    expect(hits).toBeGreaterThan(0);
    expect(lastGap).toBeLessThan(firstGap);
  });
});
