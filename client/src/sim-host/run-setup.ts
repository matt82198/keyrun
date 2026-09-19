/**
 * client/src/sim-host/run-setup.ts
 *
 * Top-level composer: turns a content-registry.ts `ContentRegistry` plus (keyLevel, dungeonId,
 * humanClassId) into everything sim/index.ts's `createRun` and worker.ts's SimHost need -- a
 * RunContent (abilities/effects/enemyScripts/combos/fieldCombos), the sim DungeonDef, and the
 * party roster (bot class kits are resolved separately, per actor, by bots/src/kits.ts -- see this
 * function's `setContentRegistry` call below). Pure and synchronous (registry-in, setup-out), so it is
 * exercised the same way whether the registry came from content-registry.ts's browser fetch loader
 * or from content/index.ts's real `loadContent()` under Node (tests use the latter -- see
 * tests/client/sim-host/run-setup.test.ts).
 *
 * content's `AbilityDef`/`EffectDef` (content/schema/ability.ts, effect.ts) are now the REAL
 * sim/combat AbilityDef/EffectDef plus a couple of harmless display/authoring extras (`name`,
 * `tags`, Chanter's `variants`) -- see those files' own docstrings -- so no field-by-field
 * conversion is needed here at all, unlike the pre-integration draft this file replaces. The one
 * real piece of work left: `variants` (Chanter's Mend/Ward stance override) has no sim-side
 * resolution mechanism (sim/combat/casting.ts's completeCast always uses `def.effects` verbatim),
 * so `resolveVariant` always substitutes the "mend" variant when present. DEVIATION: Chanter
 * therefore always plays as if permanently in Mend (heals); Refrain's own stance-swap burst and
 * every Ward-only behaviour never fire in this playable-v1.
 *
 * `lockoutTicks` is set to 80 (4s at 20Hz): every interrupt ability in content/data carries
 * `{op:"interrupt"}` with no lockout of its own (sim's cast lockout is RunContent-global, not
 * per-ability) and the pre-integration content draft's own interrupt abilities all agreed on 80
 * ticks, which this value reproduces.
 *
 * Also calls bots/src/kits.ts's `setContentRegistry(registry)`: bots' ClassKit resolution
 * (rotation priority, Setup/Payoff ids, resource economics) is injected-registry-based rather
 * than loaded internally (bots/ is bundled into the browser via worker.ts, so it cannot import
 * content/index.ts's node:fs-based loadContent() itself) -- this is the one call site that wires
 * it, since every SimHost construction calls buildRunSetup exactly once.
 *
 * Usage: `const setup = buildRunSetup(registry, {keyLevel: 5, dungeonId: "hollow-vault", humanClassId: "warden"});`
 */
import type { PartyMemberSpec, RunContent, RunOptions } from "../../../sim/index.js";
import type { DungeonDef as SimDungeonDef } from "../../../sim/encounter/pulls.js";
import type { AbilityDef as SimAbilityDef } from "../../../sim/combat/abilities.js";
import type { EffectDef as SimEffectDef } from "../../../sim/combat/effects.js";
import type { ActorId } from "../../../sim/core/state.js";
import type { Bounds } from "../../../sim/combat/movement.js";
import type { AbilityDef as ContentAbilityDef } from "../../../content/schema/ability.js";
import type { ContentRegistry } from "../../../content/schema/registry.js";
import { applyLoadout } from "../../../content/apply.js";
import { setContentRegistry } from "../../../bots/src/kits.js";
import { buildDungeon } from "./content-dungeon.js";
import { buildParty } from "./content-party.js";
import { buildComboTables } from "./content-combos.js";
import type { RawLoadout } from "./loadout-storage.js";

const INTERRUPT_LOCKOUT_TICKS = 80;
const BASE_SPEED_MM_PER_TICK = 180; // ~3.6 m/s at 20Hz
const ACTOR_RADIUS_MM = 400; // matches client/src/render/actors.ts's CAPSULE_RADIUS_M (0.4m)
// Generous enough to cover every Hollow Vault pull point plus the boss room (content-dungeon.ts's
// BOSS_SPAWN) with margin -- content's dungeon data has no arena-size field of its own.
const DUNGEON_BOUNDS: Bounds = { minX: -4000, maxX: 18000, minZ: -6000, maxZ: 9000 };

/** See file docstring: Chanter's stance-differentiated abilities always resolve to their "mend"
 * variant. Every other ability (no `variants`) passes through unchanged. */
function resolveVariant(a: ContentAbilityDef): SimAbilityDef {
  return { ...a, effects: a.variants?.mend ?? a.effects };
}

export interface RunSetupOptions {
  keyLevel: number;
  dungeonId: string;
  humanClassId: string;
  /** run-options-wiring: a `registry.clauses` id (boot-params.ts's `resolveRunSetupOptions`
   * validates it against the registry before this ever sees it -- see that file). */
  clauseId?: string;
}

export interface RunSetup {
  content: RunContent;
  dungeon: SimDungeonDef;
  party: PartyMemberSpec[];
  humanActorId: ActorId;
  tankActorId: ActorId;
  bossActorId: ActorId;
  keyLevel: number;
  parTicks: number;
  /** run-options-wiring: the human's Clause pick + loadout-resolved abilities, ready to pass
   * straight to `sim/index.ts`'s `createRun` as its 5th argument. */
  runOptions: RunOptions;
}

/** run-options-wiring: resolves the human's persisted loadout (content/apply.ts's `applyLoadout`,
 * against this registry) into `RunOptions.loadouts`'s per-actor shape, with the same
 * `resolveVariant` substitution every other ability in this run's content gets. `null`/`undefined`
 * `loadout` (no persisted build -- loadout-storage.ts's `readPersistedLoadout`) leaves the human on
 * their class's base kit, unchanged from before this addition. */
function resolveHumanLoadout(
  registry: ContentRegistry,
  humanClassId: string,
  loadout: RawLoadout | null | undefined,
): Record<string, SimAbilityDef> | undefined {
  if (!loadout) return undefined;
  const classDef = registry.classes.get(humanClassId);
  if (!classDef) return undefined; // boot-params.ts already validated humanClassId against this registry
  const result = applyLoadout(classDef, registry.abilities, registry.facets, loadout.talentChoices, loadout.gear);
  const resolved: Record<string, SimAbilityDef> = {};
  for (const [id, def] of Object.entries(result.abilities)) resolved[id] = resolveVariant(def);
  return resolved;
}

export function buildRunSetup(registry: ContentRegistry, opts: RunSetupOptions, loadout?: RawLoadout | null): RunSetup {
  setContentRegistry(registry);

  const abilities: Record<string, SimAbilityDef> = {};
  for (const [id, def] of registry.abilities) abilities[id] = resolveVariant(def);

  const effects: Record<string, SimEffectDef> = {};
  for (const [id, def] of registry.effects) effects[id] = def;

  const classIds = [...registry.classes.keys()];
  const { combos, fieldCombos } = buildComboTables(registry, classIds);

  const dungeonBuild = buildDungeon(registry, opts.dungeonId, opts.keyLevel);
  const partyBuild = buildParty(registry, opts.humanClassId);

  const lastPull = dungeonBuild.dungeon.pulls[dungeonBuild.dungeon.pulls.length - 1];
  const bossActorId = lastPull?.spawns[0]?.actorId ?? -1;

  // run-options-wiring: content DATA passed straight through (sim never imports content).
  const clauses: RunContent["clauses"] = {};
  for (const [id, def] of registry.clauses) clauses[id] = def;

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
    combos,
    fieldCombos,
    clauses,
  };

  const humanLoadout = resolveHumanLoadout(registry, opts.humanClassId, loadout);
  const runOptions: RunOptions = {
    ...(opts.clauseId !== undefined ? { clauseId: opts.clauseId } : {}),
    ...(humanLoadout ? { loadouts: { [partyBuild.humanActorId]: humanLoadout } } : {}),
  };

  return {
    content,
    dungeon: dungeonBuild.dungeon,
    party: partyBuild.party,
    humanActorId: partyBuild.humanActorId,
    tankActorId: partyBuild.tankActorId,
    bossActorId,
    keyLevel: opts.keyLevel,
    parTicks: dungeonBuild.parTicks,
    runOptions,
  };
}
