/**
 * bots/src/kits.ts
 *
 * Builds a ClassKit -- the bot-facing, decision-ready view of a class -- from content/'s
 * ClassDef + AbilityDef registries (content/schema/*.ts, content/data/classes/*.json,
 * content/data/abilities/*.json). Content is the single source of truth for rotation, resource
 * loop, and Setup/Payoff/interrupt/defensive/dispel/movement ability ids; this module never
 * invents ability data of its own.
 *
 * bots/ has zero compile-time dependency on sim/ (see types.ts's header) -- and, as of this file,
 * zero compile-time dependency on content/'s *runtime* loader either: content/index.ts's
 * loadContent() reads JSON via node:fs, but bots/ is bundled into the browser through
 * client/src/sim-host/worker.ts (its own header: "no content/ package import ... sim never
 * imports content"), which now transitively imports bot.ts -> roles.ts -> kits.ts. Statically
 * importing loadContent() here breaks that browser bundle (node:fs doesn't resolve for a browser
 * target) even if the call is never reached. So kits.ts takes its ContentRegistry by injection
 * (setContentRegistry() below) instead of loading it itself -- Node-side callers (tests, or a
 * future server host) call `setContentRegistry(loadContent())` once; a browser bot with no
 * registry injected gets `getClassKit() -> null` for every classId, which kit-resolve.ts's
 * resolveKit() already falls back from gracefully (exactly a scripted-test view's own behaviour).
 *
 * A note on drift (content/CLAUDE.md "KNOWN DRIFT"): sim/combat's real AbilityDef has no
 * `tags`/`window`/`combo` yet -- Setup/Payoff/Window is unimplemented there. This dispatch's
 * brief says to follow the tag conventions (`setup:<windowId>`, `payoff`, `interrupt`, `control`,
 * `dispel`, `defensive`, `movement`) regardless, since content/schema/ability.ts *does* carry
 * them field-for-field and bots/ depends on content/'s data shape, not sim/. Noted, not silently
 * reconciled.
 *
 * Rotation-priority tiering lives in kit-priority.ts, resource-economics helpers in
 * kit-resources.ts, the injectable-registry state in kit-registry.ts, and the
 * EffectiveKit/resolveKit() fallback in kit-resolve.ts -- split out to stay under the repo's
 * <=190 LOC/file cap. All are re-exported here so existing imports of
 * `getClassKit`/`resolveKit`/`EffectiveKit`/`setContentRegistry` from "./kits.js" keep working
 * unchanged.
 *
 * Usage: `import { getClassKit, resolveKit } from "./kits.js"`; Node-side callers additionally
 * `import { loadContent } from "@content/index"; setContentRegistry(loadContent());` once.
 */
import type { AbilityDef } from "@content/schema/ability";
import type { ClassDef, LoopKind } from "@content/schema/class";
import { abilityTags, buildRotationPriority, SETUP_TAG_PREFIX, PAYOFF_TAG } from "./kit-priority.js";
import { resourceEffectDelta, costAmount, healsOnCast } from "./kit-resources.js";

export { resolveKit, type EffectiveKit } from "./kit-resolve.js";
export { setContentRegistry, getContentRegistry, getClassKit, priorityInterruptAbilityIds } from "./kit-registry.js";

const ROT_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;
const DISPEL_TAG = "dispel";
/** Local convention: the enemy-ability tag marking a cast that must be prioritised for kicks (BIBLE.md Sec 9 "The Choir"). */
export const PRIORITY_INTERRUPT_TAG = "healer";

export interface ClassKit {
  classId: string;
  loopKind: LoopKind;
  resource: string;
  resourceMax: number;
  /** Rotational (rot1..rot5) ability ids, base priority order -- see kit-priority.ts's buildRotationPriority(). */
  rotation: string[];
  setupAbilityIds: string[];
  payoffAbilityIds: string[];
  /** Rotational ability id -> the windowId its "setup:<windowId>" tag opens. */
  windowIdBySetupAbility: Record<string, string>;
  /** Rotational ability ids whose explicit resource-effect delta is positive (builders). */
  resourceBuilderAbilityIds: string[];
  /** Rotational ability ids whose explicit resource-effect delta is negative AND are tagged Payoff (controlled dumps, e.g. Cold Snap). */
  resourceDumpAbilityIds: string[];
  /** Rotational ability id -> resource cost to cast (0 if free). */
  costByAbility: Record<string, number>;
  /** Rotational ability id -> net explicit resource-effect delta (0 if the ability has none). */
  resourceGainByAbility: Record<string, number>;
  interruptAbilityId: string;
  defensiveAbilityId: string;
  utilityAbilityId: string;
  dispelAbilityId: string | null;
  movementAbilityId: string;
  /** A rotational ability whose (or whose "mend" variant's) effects heal -- Chanter's Refrain/Chorus. Null for non-healing classes. */
  healAbilityId: string | null;
  /** A neutral (non-Setup, non-Payoff), self-targeted, resource-spending rotational ability -- Warden's Vigil. Null if none. */
  selfSustainAbilityId: string | null;
  /** For a "stance" loop (Chanter): the neutral rotational ability that flips the stance resource -- Refrain. Null for non-stance classes. */
  stanceSwapAbilityId: string | null;
}

export function buildClassKit(cls: ClassDef, abilities: ReadonlyMap<string, AbilityDef>): ClassKit {
  const get = (id: string): AbilityDef => {
    const def = abilities.get(id);
    if (!def) throw new Error(`kits: class "${cls.id}" references unknown ability "${id}"`);
    return def;
  };
  const rotationalDefs = ROT_SLOTS.map((slot) => get(cls.abilities[slot]));
  const resource = cls.loop.resource;

  const setupAbilityIds: string[] = [];
  const payoffAbilityIds: string[] = [];
  const windowIdBySetupAbility: Record<string, string> = {};
  const resourceBuilderAbilityIds: string[] = [];
  const resourceDumpAbilityIds: string[] = [];
  const costByAbility: Record<string, number> = {};
  const resourceGainByAbility: Record<string, number> = {};
  let healAbilityId: string | null = null;
  let selfSustainAbilityId: string | null = null;
  let stanceSwapAbilityId: string | null = null;

  for (const def of rotationalDefs) {
    const tags = abilityTags(def);
    const isPayoff = tags.includes(PAYOFF_TAG);
    const setupTag = tags.find((t) => t.startsWith(SETUP_TAG_PREFIX));
    if (setupTag) {
      setupAbilityIds.push(def.id);
      windowIdBySetupAbility[def.id] = setupTag.slice(SETUP_TAG_PREFIX.length);
    }
    if (isPayoff) payoffAbilityIds.push(def.id);

    const gain = resourceEffectDelta(def);
    resourceGainByAbility[def.id] = gain;
    const cost = costAmount(def, resource);
    costByAbility[def.id] = cost;
    if (gain > 0) resourceBuilderAbilityIds.push(def.id);
    if (gain < 0 && isPayoff) resourceDumpAbilityIds.push(def.id);

    if (healAbilityId === null && healsOnCast(def)) healAbilityId = def.id;
    if (selfSustainAbilityId === null && !setupTag && !isPayoff && def.shape.kind === "self" && cost > 0) {
      selfSustainAbilityId = def.id;
    }
    if (
      cls.loop.kind === "stance" &&
      stanceSwapAbilityId === null &&
      !setupTag &&
      !isPayoff &&
      gain > 0
    ) {
      stanceSwapAbilityId = def.id;
    }
  }

  const utilityDef = abilities.get(cls.abilities.utility);
  const dispelId = utilityDef && abilityTags(utilityDef).includes(DISPEL_TAG) ? cls.abilities.utility : null;

  return {
    classId: cls.id,
    loopKind: cls.loop.kind,
    resource,
    resourceMax: cls.loop.max,
    rotation: buildRotationPriority(rotationalDefs, resource),
    setupAbilityIds,
    payoffAbilityIds,
    windowIdBySetupAbility,
    resourceBuilderAbilityIds,
    resourceDumpAbilityIds,
    costByAbility,
    resourceGainByAbility,
    interruptAbilityId: cls.abilities.interrupt,
    defensiveAbilityId: cls.abilities.defensive,
    utilityAbilityId: cls.abilities.utility,
    dispelAbilityId: dispelId,
    movementAbilityId: cls.abilities.movement,
    healAbilityId,
    selfSustainAbilityId,
    stanceSwapAbilityId,
  };
}
