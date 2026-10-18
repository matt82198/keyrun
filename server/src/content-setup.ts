/**
 * server/src/content-setup.ts
 *
 * `loadServerRegistry()` loads content ONCE per process (boot); `buildServerContent(registry,
 * dungeonId, keyLevel)` builds a fresh, key-level-scaled `RunContent` + expanded dungeon
 * (`sim/encounter/pulls.ts` DungeonDef + per-actor EnemyScripts, boss pull included) per ROOM --
 * split the same way client/src/sim-host does (content-registry.ts's browser fetch loader, once,
 * vs. run-setup.ts's `buildRunSetup` per run), so a fresh room at a different key level never
 * re-reads content/data's JSON files. Mirrors client/src/sim-host/run-setup.ts +
 * content-dungeon.ts's shape field-for-field (read-only reference, not imported -- see
 * server/CLAUDE.md), INCLUDING that file's sim/encounter/tempo.ts scaling (enemy health, cast
 * cadence, telegraph lead time) -- ported in here rather than shared, since content-dungeon.ts
 * lives in client/**, out of this lane's ownership (server/CLAUDE.md's prior DEVIATION note on
 * this file: "a follow-up lane should port that scaling in").
 *
 * Also calls bots/src/kits.ts's `setContentRegistry` -- exactly the "future server host" call site
 * that file's own docstring names.
 *
 * Usage: `const registry = loadServerRegistry();`
 *        `const sc = buildServerContent(registry, DUNGEON_ID, 5);`
 */
import type { RunContent } from "../../sim/index";
import type { DungeonDef as SimDungeonDef, PullDef as SimPullDef, PullSpawn as SimPullSpawn } from "../../sim/encounter/pulls";
import type { EnemyScript, ScriptStep } from "../../sim/encounter/scripts";
import type { ActorId } from "../../sim/core/state";
import type { Bounds } from "../../sim/combat/movement";
import type { AbilityDef as SimAbilityDef } from "../../sim/combat/abilities";
import type { EffectDef as SimEffectDef } from "../../sim/combat/effects";
import type { AbilityDef as ContentAbilityDef } from "../../content/schema/ability";
import type { ContentRegistry } from "../../content/schema/registry";
import { loadContent } from "../../content/index";
import { toComboTables, toSimDungeon } from "../../content/sim-adapt";
import { applyLoadout } from "../../content/apply";
import { setContentRegistry } from "../../bots/src/kits.js";
import { EVERY_FLOOR_TICKS, scaleCastEvery, scaleHealth, scaleLeadTicks } from "../../sim/encounter/tempo";
import type { LoadoutPayload } from "./protocol.js";

export const DUNGEON_ID = "hollow-vault";
export const DEFAULT_KEY_LEVEL = 2; // lowest real key -- key 1 is the tutorial/no-scaling floor.
const BOSS_ACTOR_ID = 900;
const PULL_ACTOR_ID_START = 100;
// Continues Hollow Vault's +x pull line (choir x=0, pair x=5000, seed x=10000 -- content/data/
// dungeons/hollow-vault.json) into a boss room, same spawn client/src/sim-host/content-dungeon.ts
// uses.
const BOSS_SPAWN = { x: 15000, y: 0, z: 0 };
const FACE_MINUS_Z = 512;
const INTERRUPT_LOCKOUT_TICKS = 80; // 4s at 20Hz -- content/data's interrupt abilities all agree on this.
const BASE_SPEED_MM_PER_TICK = 180; // ~3.6 m/s at 20Hz
const ACTOR_RADIUS_MM = 400;
const DUNGEON_BOUNDS: Bounds = { minX: -4000, maxX: 18000, minZ: -6000, maxZ: 9000 };

/** Chanter's stance-differentiated abilities (`variants`) have no sim-side resolution mechanism
 * (sim/combat/casting.ts's completeCast always uses `def.effects` verbatim) -- always substitute
 * the "mend" variant, same DEVIATION run-setup.ts documents. Exported (run-options-wiring): a
 * loadout's applyLoadout-patched abilities need this SAME substitution before they reach
 * `RunContent.abilities` -- see room.ts's constructor. */
export function resolveVariant(a: ContentAbilityDef): SimAbilityDef {
  return { ...a, effects: a.variants?.mend ?? a.effects };
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

function buildDungeon(
  registry: ContentRegistry,
  dungeonId: string,
  keyLevel: number
): { dungeon: SimDungeonDef; enemyScripts: Record<ActorId, EnemyScript>; parTicks: number; bossActorId: ActorId } {
  const dungeonDef = registry.dungeons.get(dungeonId);
  if (!dungeonDef) throw new Error(`content-setup: unknown dungeon "${dungeonId}"`);

  const expanded = toSimDungeon(dungeonDef, registry.enemies, PULL_ACTOR_ID_START);
  const pulls: SimPullDef[] = expanded.dungeon.pulls.map((pull) => ({ ...pull, spawns: scaleSpawns(pull.spawns, keyLevel) }));
  const enemyScripts: Record<ActorId, EnemyScript> = {};
  for (const [actorId, script] of Object.entries(expanded.enemyScripts)) {
    enemyScripts[Number(actorId)] = scaleScript(script, keyLevel);
  }

  const bossDef = registry.enemies.get(dungeonDef.boss.enemyId);
  if (!bossDef) throw new Error(`content-setup: dungeon "${dungeonId}" boss references unknown enemy "${dungeonDef.boss.enemyId}"`);
  pulls.push({
    id: "boss",
    spawns: [{ actorId: BOSS_ACTOR_ID, classId: bossDef.id, pos: BOSS_SPAWN, yaw: FACE_MINUS_Z, health: scaleHealth(bossDef.maxHealth, keyLevel) }],
  });
  enemyScripts[BOSS_ACTOR_ID] = scaleScript(bossDef.script, keyLevel);

  return { dungeon: { id: dungeonDef.id, pulls }, enemyScripts, parTicks: dungeonDef.parTicks, bossActorId: BOSS_ACTOR_ID };
}

export interface ServerContent {
  registry: ContentRegistry;
  content: RunContent;
  dungeon: SimDungeonDef;
  parTicks: number;
  bossActorId: ActorId;
  keyLevel: number;
}

/** Boot-time, once per process: reads content/data's JSON and wires bots/src/kits.ts's registry. */
export function loadServerRegistry(): ContentRegistry {
  const registry = loadContent();
  setContentRegistry(registry);
  return registry;
}

/** Per-room: builds a fresh RunContent + dungeon scaled for `keyLevel`. Cheap (no file I/O) --
 * safe to call once per room, even many rooms per process. */
export function buildServerContent(registry: ContentRegistry, dungeonId: string, keyLevel: number): ServerContent {
  const abilities: Record<string, SimAbilityDef> = {};
  for (const [id, def] of registry.abilities) abilities[id] = resolveVariant(def);

  const effects: Record<string, SimEffectDef> = {};
  for (const [id, def] of registry.effects) effects[id] = def;

  const classIds = [...registry.classes.keys()];
  const { combos, fieldCombos } = toComboTables(registry.combos, classIds);
  const { dungeon, enemyScripts, parTicks, bossActorId } = buildDungeon(registry, dungeonId, keyLevel);

  // run-options-wiring: content DATA passed straight through (sim never imports content) so
  // createRun's options.clauseId (server/src/room.ts) can resolve a real ClauseDef.
  const clauses: RunContent["clauses"] = {};
  for (const [id, def] of registry.clauses) clauses[id] = def;

  const content: RunContent = {
    abilities,
    effects,
    lockoutTicks: INTERRUPT_LOCKOUT_TICKS,
    baseSpeedMmPerTick: BASE_SPEED_MM_PER_TICK,
    actorRadiusMm: ACTOR_RADIUS_MM,
    bounds: DUNGEON_BOUNDS,
    enemyScripts,
    keyLevel,
    parTicks,
    combos,
    fieldCombos,
    clauses,
  };

  return { registry, content, dungeon, parTicks, bossActorId, keyLevel };
}

/** Convenience for tests/one-off callers that just want a single default-key-level ServerContent
 * without managing the registry themselves -- `loadServerRegistry()` + `buildServerContent()`. */
export function loadServerContent(dungeonId: string = DUNGEON_ID, keyLevel: number = DEFAULT_KEY_LEVEL): ServerContent {
  return buildServerContent(loadServerRegistry(), dungeonId, keyLevel);
}

/** run-options-wiring: resolves ONE actor's loadout (content/apply.ts's `applyLoadout`, given the
 * room's real class/ability/facet registries) into `RunOptions.loadouts`'s per-actor shape --
 * already-loadout-resolved AbilityDef records, with the SAME `resolveVariant` substitution every
 * other ability in this room's content gets. Propagates `applyLoadout`'s own thrown Error for an
 * invariant-breaking loadout (a talent/facet id this room's content does not have, or a loadout
 * that would leave the wrong Setup/Payoff mix) -- server/src/room.ts's constructor lets that
 * surface up through lobby.ts to server/src/main.ts's queue handler, which turns it into a clean
 * `error` ServerMessage, never a crash. */
export function resolveLoadoutAbilities(registry: ContentRegistry, classId: string, loadout: LoadoutPayload): Record<string, SimAbilityDef> {
  const classDef = registry.classes.get(classId);
  if (!classDef) throw new Error(`content-setup: unknown class "${classId}" for a loadout`);
  const result = applyLoadout(classDef, registry.abilities, registry.facets, loadout.talentChoices, loadout.gear);
  const abilities: Record<string, SimAbilityDef> = {};
  for (const [id, def] of Object.entries(result.abilities)) abilities[id] = resolveVariant(def);
  return abilities;
}
