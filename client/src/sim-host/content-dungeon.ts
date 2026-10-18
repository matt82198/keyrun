/**
 * client/src/sim-host/content-dungeon.ts
 *
 * Builds the full sim/encounter DungeonDef + per-actor EnemyScripts for Hollow Vault, on top of
 * content/sim-adapt.ts's official `toSimDungeon` converter (browser-safe: pure logic, only
 * type-only imports -- see content-registry.ts's docstring for why that matters). `toSimDungeon`
 * expands content's `DungeonDef.pulls` (the authoring recipe) but has NO notion of
 * `DungeonDef.boss` at all (content/schema/dungeon.ts: "content's DungeonDef" carries `pulls` and
 * a separate `boss: BossEncounter` field `toSimDungeon` never reads) -- this file appends the boss
 * as one more pull itself, at an actorId range `toSimDungeon`'s own numbering (starting at 100)
 * can never reach.
 *
 * Applies sim/encounter/tempo.ts's key-level Tempo scaling (health, enemy cast cadence, telegraph
 * lead time) on top of both -- nothing in sim/index.ts's createRun/stepRun or content/sim-adapt.ts
 * applies Tempo automatically; that is this integration layer's job. Content's enemy scripts are
 * now plain `"loop" | number` sim ScriptSteps with no phase-offset numbers (the pre-integration
 * draft's `{at:0,every:400}` vs `{at:200,every:400}` phasing was rewritten to two same-phase loops
 * once the real sim landed), so scaling is a direct per-step map -- no step expansion needed.
 *
 * Sundered Twins (hollowWarden.twinA/twinB) are intentionally NOT spawned: their Break bar
 * mechanic needs sim/encounter/breakbar.ts wired to interrupt/control-tagged casts, which nothing
 * in sim/index.ts's stepRun does yet (content/schema/enemy.ts's own file docstring flags this as a
 * BACKLOG item). Hollow Vault's boss fight runs Cleave + The Lock's shield only.
 *
 * Usage: `const { dungeon, enemyScripts, parTicks } = buildDungeon(registry, "hollow-vault", keyLevel);`
 */
import { toSimDungeon } from "../../../content/sim-adapt.js";
import type { DungeonDef as SimDungeonDef, PullDef as SimPullDef, PullSpawn as SimPullSpawn } from "../../../sim/encounter/pulls.js";
import type { EnemyScript, ScriptStep } from "../../../sim/encounter/scripts.js";
import type { ActorId } from "../../../sim/core/state.js";
import { EVERY_FLOOR_TICKS, scaleCastEvery, scaleHealth, scaleLeadTicks } from "../../../sim/encounter/tempo.js";
import type { ContentRegistry } from "../../../content/schema/registry.js";

const PULL_ACTOR_ID_START = 100;
const BOSS_ACTOR_ID = 900;
// The dungeon data carries no boss spawn point (only `boss.enemyId`); Hollow Vault's pulls run
// along +x (choir near x=0, pair at x=5000, seed at x=10000 -- see content/data/dungeons/hollow-
// vault.json), so the boss room continues that line.
const BOSS_SPAWN = { x: 15000, y: 0, z: 0 };
const FACE_MINUS_Z = 512; // yaw 512 = facing -z (telegraphs.ts: yaw 0 faces +z), toward the party

export interface DungeonBuild {
  dungeon: SimDungeonDef;
  enemyScripts: Record<ActorId, EnemyScript>;
  parTicks: number;
}

function scaleStep(step: ScriptStep, keyLevel: number): ScriptStep {
  const telegraph = step.telegraph ? { telegraph: { ...step.telegraph, leadTicks: scaleLeadTicks(step.telegraph.leadTicks, keyLevel) } } : {};
  if (step.at === "loop") {
    return { at: "loop", every: scaleCastEvery(step.every ?? EVERY_FLOOR_TICKS, keyLevel), cast: step.cast, ...telegraph };
  }
  const every = step.every !== undefined ? { every: scaleCastEvery(step.every, keyLevel) } : {};
  return { at: step.at, ...every, cast: step.cast, ...telegraph };
}

function scaleScript(script: EnemyScript, keyLevel: number): EnemyScript {
  return script.map((step) => scaleStep(step, keyLevel));
}

function scaleSpawns(spawns: SimPullSpawn[], keyLevel: number): SimPullSpawn[] {
  return spawns.map((s) => ({ ...s, health: scaleHealth(s.health, keyLevel) }));
}

export function buildDungeon(registry: ContentRegistry, dungeonId: string, keyLevel: number): DungeonBuild {
  const dungeonDef = registry.dungeons.get(dungeonId);
  if (!dungeonDef) throw new Error(`content-dungeon: unknown dungeon "${dungeonId}"`);

  const expanded = toSimDungeon(dungeonDef, registry.enemies, PULL_ACTOR_ID_START);
  const pulls: SimPullDef[] = expanded.dungeon.pulls.map((pull) => ({ ...pull, spawns: scaleSpawns(pull.spawns, keyLevel) }));
  const enemyScripts: Record<ActorId, EnemyScript> = {};
  for (const [actorId, script] of Object.entries(expanded.enemyScripts)) {
    enemyScripts[Number(actorId)] = scaleScript(script, keyLevel);
  }

  const bossDef = registry.enemies.get(dungeonDef.boss.enemyId);
  if (!bossDef) throw new Error(`content-dungeon: dungeon "${dungeonId}" boss references unknown enemy "${dungeonDef.boss.enemyId}"`);
  pulls.push({
    id: "boss",
    spawns: [{ actorId: BOSS_ACTOR_ID, classId: bossDef.id, pos: BOSS_SPAWN, yaw: FACE_MINUS_Z, health: scaleHealth(bossDef.maxHealth, keyLevel) }],
  });
  enemyScripts[BOSS_ACTOR_ID] = scaleScript(bossDef.script, keyLevel);

  return { dungeon: { id: dungeonDef.id, pulls }, enemyScripts, parTicks: dungeonDef.parTicks };
}
