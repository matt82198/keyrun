/**
 * tests/sim/integration/enemy-casts.test.ts — P0 regression: enemy script casts must reach a live
 * target through the REAL public path (sim/index.ts's createRun/stepRun, real content loaded via
 * content/index.ts's loadContent, no test-only ability stand-ins). Before the fix, every enemy
 * ability in content/data/abilities/enemies.json is target-shaped ("shape.kind": "target"); the
 * enemy scripts in sim/encounter/scripts.ts never supplied a targetId, so sim/combat/casting.ts's
 * startCast refused every one of them ("target_required") and sim/core/tick.ts silently dropped the
 * refused command -- the dungeon was inert (0 enemy casts land, 0 damage to the party) despite three
 * enemies spawned and standing within melee range of the party the entire run.
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type PartyMemberSpec, type Run, type RunContent } from "@sim/index";
import type { SimEvent } from "@sim/core/events";
import type { DungeonDef as SimDungeonDef } from "@sim/encounter/pulls";
import { loadContent, toSimDungeon } from "@content/index";

const SEED = 778899;
const TICKS = 500;
const PLAYER_ID = 500;
const FIRST_ENEMY_ACTOR_ID = 2000;

function buildRun(): { run: Run; enemyIds: Set<number> } {
  const reg = loadContent();
  const abilities = Object.fromEntries(reg.abilities);
  const effects = Object.fromEntries(reg.effects);
  const bounds = { minX: -20000, maxX: 20000, minZ: -20000, maxZ: 20000 };

  // Hollow Vault's first pull, "The Choir" (design/BIBLE.md §9): two choir.melee + one
  // choir.lieutenant, spawned via the real content->sim expander so actor ids/positions match
  // content/data/dungeons/hollow-vault.json exactly.
  const dungeonDef = reg.dungeons.get("hollow-vault")!;
  const expanded = toSimDungeon(dungeonDef, reg.enemies, FIRST_ENEMY_ACTOR_ID);
  const dungeon: SimDungeonDef = { id: expanded.dungeon.id, pulls: [expanded.dungeon.pulls[0]!] };

  // Party stands in melee range (600mm) of the choir.melee pair (spawned at x:0, z:1000) and well
  // within choir.healCast's range of the lieutenant (spawned at x:0, z:-1000) -- standing still,
  // never moving or casting, so any damage the party takes can only come from the enemies' own
  // scripted casts landing through the real cast pipeline.
  const party: PartyMemberSpec[] = [
    { id: PLAYER_ID, classId: "warden", role: "tank", pos: { x: 0, y: 0, z: 400 }, maxHealth: 2000, maxResource: 150 },
  ];

  const content: RunContent = {
    abilities,
    effects,
    lockoutTicks: 80,
    baseSpeedMmPerTick: 180,
    actorRadiusMm: 400,
    bounds,
    enemyScripts: expanded.enemyScripts,
  };

  const run = createRun(SEED, party, dungeon, content);
  return { run, enemyIds: new Set(Object.keys(expanded.enemyScripts).map(Number)) };
}

describe("enemy scripted casts reach a live target (P0 regression)", () => {
  it("enemies actually cast and damage the party over 500 ticks via createRun/stepRun", () => {
    const { run: initialRun, enemyIds } = buildRun();
    let run = initialRun;
    const events: SimEvent[] = [];
    for (let t = 0; t < TICKS; t += 1) {
      const stepped = stepRun(run, []);
      run = stepped.run;
      events.push(...stepped.events);
    }

    const enemyCastCompletes = events.filter(
      (e) => e.type === "cast_complete" && enemyIds.has(e.actorId as number),
    );
    const damageOnPlayer = events.filter((e) => e.type === "damage" && e.targetId === PLAYER_ID);

    expect(enemyCastCompletes.length).toBeGreaterThanOrEqual(1);
    expect(damageOnPlayer.length).toBeGreaterThanOrEqual(1);

    const player = run.state.actors[PLAYER_ID]!;
    expect(player.health).toBeLessThan(player.maxHealth);
  });
});
