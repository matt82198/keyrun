/**
 * client/src/sim-host/content-dungeon.ts
 *
 * Builds a sim/encounter DungeonDef (spawns) and per-actor EnemyScripts from content's Hollow
 * Vault dungeon + enemy data, applying sim/encounter/tempo.ts's key-level scaling (the sim ships
 * these pure scale functions but nothing in sim/index.ts's createRun/stepRun calls them --
 * applying Tempo to raw content numbers is this integration layer's job).
 *
 * Two content/sim shape gaps bridged here (documented, not silently patched):
 *  - sim/encounter/scripts.ts's ScriptStep is `{at: "loop" | number, every?, cast, telegraph?}`:
 *    "loop" repeats every `every` ticks starting at tick `every` (never a phase-offset first
 *    fire); a bare number fires exactly once. content's ScriptStep additionally uses a NUMBER
 *    `at` together with `every` to mean "first fire at `at`, repeat every `every` after" (see
 *    pair.spreadMark at:0/every:400 vs pair.stackCall at:200/every:400 -- two different phases of
 *    the same 400-tick cycle). sim has no such phase-offset primitive, so this file EXPANDS that
 *    pattern into explicit one-shot numeric steps (`at`, `at+every`, `at+2*every`, ...) out to a
 *    horizon tick (the dungeon's own parTicks plus a buffer), which reproduces the exact same
 *    firing ticks without touching sim/encounter/scripts.ts.
 *  - content's `"onDeath"` script trigger (The Seed's floor-Window loot drop) has no sim
 *    equivalent (ScriptStep.at is `"loop" | number` only) -- DROPPED. The Seed still casts its
 *    slam on loop; its death-triggered Window never opens in this playable-v1.
 *
 * Enemies never auto-target: sim/combat has no nearest-enemy AI (only sim/combat/targeting.ts's
 * `cycleTarget`, a manual tab-cycle helper) -- see worker.ts's own comment for how targets get
 * assigned at spawn time.
 *
 * Sundered Twins (hollowWarden.twinA/twinB) are intentionally NOT spawned: their Break bar
 * mechanic keys off "any interrupt- or control-tagged ability", but sim's AbilityDef carries no
 * `tags` field at all (content/CLAUDE.md's documented drift) -- there is no data path for sim to
 * know which cast should drain a Break bar. Hollow Vault's boss fight therefore runs Cleave + The
 * Lock's shield only, not the add phase; filed as a backlog item, not silently faked.
 *
 * Usage: `const { dungeon, enemyScripts } = buildDungeon(registry, "hollow-vault", keyLevel);`
 */
import type { DungeonDef as SimDungeonDef, PullDef as SimPullDef, PullSpawn as SimPullSpawn } from "../../../sim/encounter/pulls.js";
import type { EnemyScript, ScriptStep } from "../../../sim/encounter/scripts.js";
import type { ActorId } from "../../../sim/core/state.js";
import { scaleCastEvery, scaleHealth, scaleLeadTicks, EVERY_FLOOR_TICKS } from "../../../sim/encounter/tempo.js";
import type { CEnemyDef, ContentRegistry } from "./content-registry.js";

const SPAWN_SPREAD_MM = 1200;
const ENEMY_ACTOR_ID_START = 100;
const HORIZON_BUFFER_TICKS = 2400; // +2 minutes past par, for numeric+every step expansion
// The dungeon data carries no boss spawn point (only `boss.enemyId`); Hollow Vault's pulls run
// along +x (choir near x=0, pair at x=5000, seed at x=10000 -- see content/data/dungeons/hollow-
// vault.json), so the boss room continues that line.
const BOSS_SPAWN = { x: 15000, z: 0 };
const FACE_MINUS_Z = 512; // yaw 512 = facing -z (telegraphs.ts: yaw 0 faces +z), toward the party

export interface DungeonBuild {
  dungeon: SimDungeonDef;
  enemyScripts: Record<ActorId, EnemyScript>;
  parTicks: number;
}

function expandStep(step: ScriptStep, keyLevel: number, horizon: number): ScriptStep[] {
  if (step.at === "loop" || typeof step.at !== "number" || step.every === undefined) {
    return [scaleLoopOrOneShot(step, keyLevel)];
  }
  const first = step.at === 0 ? 0 : scaleCastEvery(step.at, keyLevel);
  const every = scaleCastEvery(step.every, keyLevel);
  const out: ScriptStep[] = [];
  for (let t = first; t <= horizon; t += every) {
    out.push({ at: t, cast: step.cast, ...(step.telegraph ? { telegraph: scaleTelegraph(step.telegraph, keyLevel) } : {}) });
  }
  return out;
}

function scaleLoopOrOneShot(step: ScriptStep, keyLevel: number): ScriptStep {
  const telegraph = step.telegraph ? { telegraph: scaleTelegraph(step.telegraph, keyLevel) } : {};
  if (step.at === "loop") {
    return { at: "loop", every: scaleCastEvery(step.every ?? EVERY_FLOOR_TICKS, keyLevel), cast: step.cast, ...telegraph };
  }
  return { at: step.at, cast: step.cast, ...telegraph };
}

function scaleTelegraph(tg: NonNullable<ScriptStep["telegraph"]>, keyLevel: number): NonNullable<ScriptStep["telegraph"]> {
  return { ...tg, leadTicks: scaleLeadTicks(tg.leadTicks, keyLevel) };
}

function buildEnemyScript(enemyDef: CEnemyDef, keyLevel: number, horizon: number): EnemyScript {
  const steps: ScriptStep[] = [];
  for (const step of enemyDef.script) {
    if (step.at === "onDeath") continue; // see file docstring
    steps.push(...expandStep(step, keyLevel, horizon));
  }
  return steps;
}

function spawnCount(
  registry: ContentRegistry,
  enemyId: string,
  count: number,
  spawn: { x: number; z: number },
  keyLevel: number,
  horizon: number,
  nextActorId: () => number,
  dungeonId: string,
  pullId: string,
): { spawns: SimPullSpawn[]; scripts: Record<ActorId, EnemyScript> } {
  const enemyDef = registry.enemies.get(enemyId);
  if (!enemyDef) throw new Error(`content-dungeon: dungeon "${dungeonId}" pull "${pullId}" references unknown enemy "${enemyId}"`);
  const spawns: SimPullSpawn[] = [];
  const scripts: Record<ActorId, EnemyScript> = {};
  for (let i = 0; i < count; i++) {
    const actorId = nextActorId();
    const offset = (i - (count - 1) / 2) * SPAWN_SPREAD_MM;
    spawns.push({
      actorId,
      classId: enemyDef.id,
      pos: { x: spawn.x + offset, y: 0, z: spawn.z },
      yaw: FACE_MINUS_Z,
      health: scaleHealth(enemyDef.maxHealth, keyLevel),
    });
    scripts[actorId] = buildEnemyScript(enemyDef, keyLevel, horizon);
  }
  return { spawns, scripts };
}

export function buildDungeon(registry: ContentRegistry, dungeonId: string, keyLevel: number): DungeonBuild {
  const dungeonDef = registry.dungeons.get(dungeonId);
  if (!dungeonDef) throw new Error(`content-dungeon: unknown dungeon "${dungeonId}"`);
  const horizon = dungeonDef.parTicks + HORIZON_BUFFER_TICKS;

  let nextId = ENEMY_ACTOR_ID_START;
  const nextActorId = (): number => nextId++;

  const pulls: SimPullDef[] = [];
  const enemyScripts: Record<ActorId, EnemyScript> = {};

  for (const pull of dungeonDef.pulls) {
    const spawns: SimPullSpawn[] = [];
    for (const entry of pull.enemies) {
      const built = spawnCount(registry, entry.enemyId, entry.count, entry.spawn, keyLevel, horizon, nextActorId, dungeonId, pull.id);
      spawns.push(...built.spawns);
      Object.assign(enemyScripts, built.scripts);
    }
    pulls.push({ id: pull.id, spawns });
  }

  const bossBuilt = spawnCount(registry, dungeonDef.boss.enemyId, 1, BOSS_SPAWN, keyLevel, horizon, nextActorId, dungeonId, "boss");
  pulls.push({ id: "boss", spawns: bossBuilt.spawns });
  Object.assign(enemyScripts, bossBuilt.scripts);

  return { dungeon: { id: dungeonDef.id, pulls }, enemyScripts, parTicks: dungeonDef.parTicks };
}
