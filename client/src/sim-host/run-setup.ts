/**
 * client/src/sim-host/run-setup.ts
 *
 * Top-level composer: turns a content-registry.ts `ContentRegistry` plus (keyLevel, dungeonId,
 * humanClassId) into everything sim/index.ts's `createRun` and worker.ts's SimHost need -- a
 * RunContent, the sim DungeonDef, the party roster, and per-class bot classKits. Pure and
 * synchronous (registry-in, setup-out), so it is exercised the same way whether the registry came
 * from content-registry.ts's browser fetch loader or from content/index.ts's real `loadContent()`
 * under Node (tests use the latter -- see tests/client/sim-host/run-setup.test.ts).
 *
 * `lockoutTicks` is set to 80 (4s at 20Hz): every interrupt ability in content/data carries
 * `{op:"interrupt", lockoutTicks:80}` identically, so this single RunContent-wide value (sim's
 * cast lockout is global, not per-ability -- see content-abilities.ts's docstring) reproduces
 * content's own intent exactly rather than picking an arbitrary number.
 *
 * Usage: `const setup = buildRunSetup(registry, {keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden"});`
 */
import type { PartyMemberSpec, RunContent } from "../../../sim/index.js";
import type { DungeonDef as SimDungeonDef } from "../../../sim/encounter/pulls.js";
import type { AbilityDef as SimAbilityDef } from "../../../sim/combat/abilities.js";
import type { EffectDef as SimEffectDef } from "../../../sim/combat/effects.js";
import type { ActorId } from "../../../sim/core/state.js";
import type { Bounds } from "../../../sim/combat/movement.js";
import type { BotView } from "../../../bots/src/types.js";
import type { ContentRegistry } from "./content-registry.js";
import { toSimAbility, toSimEffectDef } from "./content-abilities.js";
import { buildDungeon } from "./content-dungeon.js";
import { buildParty } from "./content-party.js";

const INTERRUPT_LOCKOUT_TICKS = 80;
const BASE_SPEED_MM_PER_TICK = 180; // ~3.6 m/s at 20Hz
const ACTOR_RADIUS_MM = 400; // matches client/src/render/actors.ts's CAPSULE_RADIUS_M (0.4m)
// Generous enough to cover every Hollow Vault pull point plus the boss room (content-dungeon.ts's
// BOSS_SPAWN) with margin -- content's dungeon data has no arena-size field of its own.
const DUNGEON_BOUNDS: Bounds = { minX: -4000, maxX: 18000, minZ: -6000, maxZ: 9000 };

const ABILITY_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5", "interrupt", "defensive", "utility", "movement"] as const;

/** abilityId -> owning class id (for player abilities) or enemy id (pseudo-class, for enemy
 * "window" ops -- see content-abilities.ts; no content/data combo ever keys off an enemy pseudo-
 * class, so its exact value only matters for readability/debugging). */
function buildAbilityOwnerMap(registry: ContentRegistry): Map<string, string> {
  const owner = new Map<string, string>();
  for (const cls of registry.classes.values()) {
    for (const slot of ABILITY_SLOTS) owner.set(cls.abilities[slot], cls.id);
  }
  for (const enemy of registry.enemies.values()) {
    for (const abilityId of enemy.abilities) {
      if (!owner.has(abilityId)) owner.set(abilityId, enemy.id);
    }
  }
  return owner;
}

export interface RunSetupOptions {
  keyLevel: number;
  dungeonId: string;
  humanClassId: string;
}

export interface RunSetup {
  content: RunContent;
  dungeon: SimDungeonDef;
  party: PartyMemberSpec[];
  humanActorId: ActorId;
  tankActorId: ActorId;
  bossActorId: ActorId;
  classKits: Record<string, BotView["classKit"]>;
  actorClassId: Record<ActorId, string>;
  keyLevel: number;
  parTicks: number;
}

export function buildRunSetup(registry: ContentRegistry, opts: RunSetupOptions): RunSetup {
  const ownerOf = buildAbilityOwnerMap(registry);

  const abilities: Record<string, SimAbilityDef> = {};
  for (const [id, def] of registry.abilities) abilities[id] = toSimAbility(def, ownerOf.get(id) ?? "none");

  const effects: Record<string, SimEffectDef> = {};
  for (const [id, def] of registry.effects) effects[id] = toSimEffectDef(def);

  const dungeonBuild = buildDungeon(registry, opts.dungeonId, opts.keyLevel);
  const partyBuild = buildParty(registry, opts.humanClassId);

  const lastPull = dungeonBuild.dungeon.pulls[dungeonBuild.dungeon.pulls.length - 1];
  const bossActorId = lastPull?.spawns[0]?.actorId ?? -1;

  const content: RunContent = {
    abilities,
    effects,
    lockoutTicks: INTERRUPT_LOCKOUT_TICKS,
    baseSpeedMmPerTick: BASE_SPEED_MM_PER_TICK,
    actorRadiusMm: ACTOR_RADIUS_MM,
    bounds: DUNGEON_BOUNDS,
    enemyScripts: dungeonBuild.enemyScripts,
    keyLevel: opts.keyLevel,
    parTicks: dungeonBuild.parTicks,
  };

  return {
    content,
    dungeon: dungeonBuild.dungeon,
    party: partyBuild.party,
    humanActorId: partyBuild.humanActorId,
    tankActorId: partyBuild.tankActorId,
    bossActorId,
    classKits: partyBuild.classKits,
    actorClassId: partyBuild.actorClassId,
    keyLevel: opts.keyLevel,
    parTicks: dungeonBuild.parTicks,
  };
}
