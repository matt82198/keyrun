/**
 * server/src/content-setup.ts
 *
 * Loads content once and builds sim/index.ts's `RunContent` plus the expanded dungeon
 * (`sim/encounter/pulls.ts` DungeonDef + per-actor EnemyScripts, boss pull included). Mirrors
 * client/src/sim-host/run-setup.ts + content-dungeon.ts's shape (read-only reference, not
 * imported -- see server/CLAUDE.md).
 *
 * DEVIATION: no sim/encounter/tempo.ts key-level scaling (the client's content-dungeon.ts applies
 * it; this lane's dungeon/party wiring is server-owned, not a client import, per the dispatch
 * brief) -- every key level runs Hollow Vault's base numbers. Noted, not silently matched; a
 * follow-up lane should port scaleHealth/scaleCastEvery/scaleLeadTicks in.
 *
 * Also calls bots/src/kits.ts's `setContentRegistry` -- exactly the "future server host" call site
 * that file's own docstring names.
 *
 * Usage: `const { registry, content, dungeon, parTicks } = loadServerContent();`
 */
import type { RunContent } from "../../sim/index";
import type { DungeonDef as SimDungeonDef, PullDef as SimPullDef } from "../../sim/encounter/pulls";
import type { EnemyScript } from "../../sim/encounter/scripts";
import type { ActorId } from "../../sim/core/state";
import type { Bounds } from "../../sim/combat/movement";
import type { AbilityDef as SimAbilityDef } from "../../sim/combat/abilities";
import type { EffectDef as SimEffectDef } from "../../sim/combat/effects";
import type { AbilityDef as ContentAbilityDef } from "../../content/schema/ability";
import type { ContentRegistry } from "../../content/schema/registry";
import { loadContent } from "../../content/index";
import { toComboTables, toSimDungeon } from "../../content/sim-adapt";
import { setContentRegistry } from "../../bots/src/kits.js";

export const DUNGEON_ID = "hollow-vault";
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
 * the "mend" variant, same DEVIATION run-setup.ts documents. */
function resolveVariant(a: ContentAbilityDef): SimAbilityDef {
  return { ...a, effects: a.variants?.mend ?? a.effects };
}

function buildDungeon(registry: ContentRegistry, dungeonId: string): { dungeon: SimDungeonDef; enemyScripts: Record<ActorId, EnemyScript>; parTicks: number } {
  const dungeonDef = registry.dungeons.get(dungeonId);
  if (!dungeonDef) throw new Error(`content-setup: unknown dungeon "${dungeonId}"`);

  const expanded = toSimDungeon(dungeonDef, registry.enemies, PULL_ACTOR_ID_START);
  const pulls: SimPullDef[] = [...expanded.dungeon.pulls];
  const enemyScripts: Record<ActorId, EnemyScript> = { ...expanded.enemyScripts };

  const bossDef = registry.enemies.get(dungeonDef.boss.enemyId);
  if (!bossDef) throw new Error(`content-setup: dungeon "${dungeonId}" boss references unknown enemy "${dungeonDef.boss.enemyId}"`);
  pulls.push({
    id: "boss",
    spawns: [{ actorId: BOSS_ACTOR_ID, classId: bossDef.id, pos: BOSS_SPAWN, yaw: FACE_MINUS_Z, health: bossDef.maxHealth }],
  });
  enemyScripts[BOSS_ACTOR_ID] = bossDef.script;

  return { dungeon: { id: dungeonDef.id, pulls }, enemyScripts, parTicks: dungeonDef.parTicks };
}

export interface ServerContent {
  registry: ContentRegistry;
  content: RunContent;
  dungeon: SimDungeonDef;
  parTicks: number;
}

export function loadServerContent(dungeonId: string = DUNGEON_ID): ServerContent {
  const registry = loadContent();
  setContentRegistry(registry);

  const abilities: Record<string, SimAbilityDef> = {};
  for (const [id, def] of registry.abilities) abilities[id] = resolveVariant(def);

  const effects: Record<string, SimEffectDef> = {};
  for (const [id, def] of registry.effects) effects[id] = def;

  const classIds = [...registry.classes.keys()];
  const { combos, fieldCombos } = toComboTables(registry.combos, classIds);
  const { dungeon, enemyScripts, parTicks } = buildDungeon(registry, dungeonId);

  const content: RunContent = {
    abilities,
    effects,
    lockoutTicks: INTERRUPT_LOCKOUT_TICKS,
    baseSpeedMmPerTick: BASE_SPEED_MM_PER_TICK,
    actorRadiusMm: ACTOR_RADIUS_MM,
    bounds: DUNGEON_BOUNDS,
    enemyScripts,
    parTicks,
    combos,
    fieldCombos,
  };

  return { registry, content, dungeon, parTicks };
}
