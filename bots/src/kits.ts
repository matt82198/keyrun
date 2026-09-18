/**
 * bots/src/kits.ts
 *
 * Builds a ClassKit -- the bot-facing, decision-ready view of a class -- from content/'s
 * ClassDef + AbilityDef registries (content/schema/*.ts, content/data/classes/*.json,
 * content/data/abilities/*.json). Content is the single source of truth for rotation, resource
 * loop, and Setup/Payoff/interrupt/defensive/dispel/movement ability ids; this module never
 * invents ability data of its own.
 *
 * bots/ has zero compile-time dependency on sim/ (see types.ts's header) but content/ is plain,
 * already-validated data with a stable id-registry contract (content/index.ts throws loudly on
 * load if anything is malformed), so kits.ts is the one place bots/ reads content/ directly.
 *
 * A note on drift (content/CLAUDE.md "KNOWN DRIFT"): sim/combat's real AbilityDef has no
 * `tags`/`window`/`combo` yet -- Setup/Payoff/Window is unimplemented there. This dispatch's
 * brief says to follow the tag conventions (`setup:<windowId>`, `payoff`, `interrupt`, `control`,
 * `dispel`, `defensive`, `movement`) regardless, since content/schema/ability.ts *does* carry
 * them field-for-field and bots/ depends on content/, not sim/. Noted, not silently reconciled.
 *
 * Usage: `import { getClassKit, resolveKit } from "./kits.js"`
 */
import { loadContent, type ContentRegistry } from "@content/index";
import type { AbilityDef } from "@content/schema/ability";
import type { ClassDef, LoopKind } from "@content/schema/class";
import type { BotView } from "./types.js";

const ROT_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;
const SETUP_TAG_PREFIX = "setup:";
const PAYOFF_TAG = "payoff";
const DISPEL_TAG = "dispel";
/** Local convention: the enemy-ability tag marking a cast that must be prioritised for kicks (BIBLE.md Sec 9 "The Choir"). */
export const PRIORITY_INTERRUPT_TAG = "healer";

export interface ClassKit {
  classId: string;
  loopKind: LoopKind;
  resource: string;
  resourceMax: number;
  /** Rotational (rot1..rot5) ability ids, base priority order -- see buildRotationPriority(). */
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

function abilityTags(def: AbilityDef): readonly string[] {
  return def.tags ?? [];
}

/**
 * Net of every explicit `{op:"resource"}` delta on `resource` in the ability's base effects.
 * Variants (content/schema/ability.ts: a per-stance *replacement* for `effects`, never additive)
 * are deliberately not summed in here -- every current variant pair (Chanter's mend/ward) agrees
 * with its base resource delta anyway, so this stays correct without double-counting.
 */
function resourceEffectDelta(def: AbilityDef, resource: string): number {
  let delta = 0;
  for (const op of def.effects) {
    if (op.op === "resource" && op.resource === resource) delta += op.delta;
  }
  return delta;
}

function costAmount(def: AbilityDef, resource: string): number {
  return def.cost && def.cost.resource === resource ? def.cost.amount : 0;
}

function healsOnCast(def: AbilityDef): boolean {
  if (def.effects.some((op) => op.op === "heal")) return true;
  return Object.values(def.variants ?? {}).some((variant) => variant.some((op) => op.op === "heal"));
}

/** Ability "weight" tier: lower sorts first. Cost-paying finishers > Payoffs > Setups > everything else. */
function tier(def: AbilityDef, resource: string): number {
  if (costAmount(def, resource) > 0) return 0;
  if (abilityTags(def).includes(PAYOFF_TAG)) return 1;
  if (abilityTags(def).some((t) => t.startsWith(SETUP_TAG_PREFIX))) return 2;
  return 3;
}

/**
 * Baseline damage-rotation priority: resource-cost finishers first (spend before you cap), then
 * Payoffs, then Setups, then filler -- ties broken by longer cooldown first (use the scarce
 * cooldown before it idles), then ability id for determinism. This is the fallback order
 * pickRotationAbility() cycles when no Window/Payoff-ally override (roles.ts) applies.
 */
export function buildRotationPriority(rotationalDefs: readonly AbilityDef[], resource: string): string[] {
  return [...rotationalDefs]
    .sort((a, b) => {
      const ta = tier(a, resource);
      const tb = tier(b, resource);
      if (ta !== tb) return ta - tb;
      if (a.cooldownTicks !== b.cooldownTicks) return b.cooldownTicks - a.cooldownTicks;
      return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
    })
    .map((d) => d.id);
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

    const gain = resourceEffectDelta(def, resource);
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

let cachedRegistry: ContentRegistry | null = null;

/** Loads (and memoises) the content registry. Throws if content/ fails to validate -- fail loud, not partial. */
export function getContentRegistry(): ContentRegistry {
  if (!cachedRegistry) cachedRegistry = loadContent();
  return cachedRegistry;
}

const kitCache = new Map<string, ClassKit>();

/**
 * The built ClassKit for `classId`, or null if `classId` isn't one of content's three classes
 * (e.g. a scripted-test actor's placeholder classId). Callers fall back to BotView.classKit in
 * that case -- see resolveKit() below.
 */
export function getClassKit(classId: string): ClassKit | null {
  const cached = kitCache.get(classId);
  if (cached) return cached;
  const reg = getContentRegistry();
  const cls = reg.classes.get(classId);
  if (!cls) return null;
  const kit = buildClassKit(cls, reg.abilities);
  kitCache.set(classId, kit);
  return kit;
}

/**
 * Enemy-ability ids tagged "healer" (BIBLE.md Sec 9 "The Choir": "a 4-second heal cast must be
 * kicked; teaches the kick ledger") -- the priority-cast list shared/ledger.ts honours ahead of
 * any other simultaneous enemy cast.
 */
export function priorityInterruptAbilityIds(): string[] {
  const reg = getContentRegistry();
  const ids: string[] = [];
  for (const ability of reg.abilities.values()) {
    if ((ability.tags ?? []).includes(PRIORITY_INTERRUPT_TAG)) ids.push(ability.id);
  }
  return ids.sort();
}

/** The superset of ClassKit fields roles.ts actually consumes, with every field optional-safe. */
export interface EffectiveKit {
  rotation: string[];
  interruptAbilityId: string | null;
  defensiveAbilityId: string | null;
  healAbilityId: string | null;
  selfSustainAbilityId: string | null;
  stanceSwapAbilityId: string | null;
  dispelAbilityId: string | null;
  setupAbilityIds: string[];
  payoffAbilityIds: string[];
  windowIdBySetupAbility: Record<string, string>;
  costByAbility: Record<string, number>;
  resourceGainByAbility: Record<string, number>;
  resourceDumpAbilityIds: string[];
  resourceBuilderAbilityIds: string[];
  loopKind: LoopKind | null;
  resource: string | null;
  resourceMax: number | null;
}

/**
 * Resolves the richest kit available for `classId`: content's real ClassKit when `classId` names
 * one of the three starter classes, otherwise a thin EffectiveKit built from BotView's own
 * (integration-lane-supplied) `classKit` -- the simple `{rotation, interruptAbilityId,
 * defensiveAbilityId, healAbilityId}` shape types.ts already carries. Keeps roles.ts working
 * against scripted-test views that use placeholder classIds (no content lookup possible) while
 * getting full Setup/Payoff/resource-aware behaviour for real warden/cinder/chanter actors.
 */
export function resolveKit(classId: string, fallback: BotView["classKit"]): EffectiveKit {
  const kit = getClassKit(classId);
  if (kit) {
    return {
      rotation: kit.rotation,
      interruptAbilityId: kit.interruptAbilityId,
      defensiveAbilityId: kit.defensiveAbilityId,
      healAbilityId: kit.healAbilityId,
      selfSustainAbilityId: kit.selfSustainAbilityId,
      stanceSwapAbilityId: kit.stanceSwapAbilityId,
      dispelAbilityId: kit.dispelAbilityId,
      setupAbilityIds: kit.setupAbilityIds,
      payoffAbilityIds: kit.payoffAbilityIds,
      windowIdBySetupAbility: kit.windowIdBySetupAbility,
      costByAbility: kit.costByAbility,
      resourceGainByAbility: kit.resourceGainByAbility,
      resourceDumpAbilityIds: kit.resourceDumpAbilityIds,
      resourceBuilderAbilityIds: kit.resourceBuilderAbilityIds,
      loopKind: kit.loopKind,
      resource: kit.resource,
      resourceMax: kit.resourceMax,
    };
  }
  return {
    rotation: fallback.rotation,
    interruptAbilityId: fallback.interruptAbilityId,
    defensiveAbilityId: fallback.defensiveAbilityId,
    healAbilityId: fallback.healAbilityId,
    selfSustainAbilityId: null,
    stanceSwapAbilityId: null,
    dispelAbilityId: null,
    setupAbilityIds: [],
    payoffAbilityIds: [],
    windowIdBySetupAbility: {},
    costByAbility: {},
    resourceGainByAbility: {},
    resourceDumpAbilityIds: [],
    resourceBuilderAbilityIds: [],
    loopKind: null,
    resource: null,
    resourceMax: null,
  };
}
