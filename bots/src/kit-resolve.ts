/**
 * bots/src/kit-resolve.ts
 *
 * EffectiveKit -- the superset of ClassKit fields roles/*.ts actually consumes -- and
 * resolveKit(), which falls back to a scripted BotView's own `classKit` for a classId content
 * doesn't define. Split out of kits.ts to stay under the repo's <=190 LOC/file cap; no behaviour
 * change.
 *
 * Usage: `import { resolveKit, type EffectiveKit } from "./kit-resolve.js"`
 */
import type { LoopKind } from "@content/schema/class";
import type { BotView } from "./types.js";
import { getClassKit } from "./kits.js";

/** The superset of ClassKit fields roles/*.ts actually consumes, with every field optional-safe. */
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
 * defensiveAbilityId, healAbilityId}` shape types.ts already carries. Keeps roles/*.ts working
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
