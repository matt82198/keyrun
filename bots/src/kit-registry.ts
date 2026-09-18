/**
 * bots/src/kit-registry.ts
 *
 * The injectable ContentRegistry state kits.ts's getClassKit()/priorityInterruptAbilityIds()
 * read from -- split out of kits.ts to stay under the repo's <=190 LOC/file cap; no behaviour
 * change. See kits.ts's header for why the registry is injected rather than loaded here directly
 * (content/index.ts's loadContent() reads node:fs; bots/ is bundled into the browser via
 * client/src/sim-host/worker.ts).
 *
 * Usage: `import { setContentRegistry, getClassKit, priorityInterruptAbilityIds } from "./kit-registry.js"`
 */
import type { ContentRegistry } from "@content/schema/registry";
import { buildClassKit, PRIORITY_INTERRUPT_TAG, type ClassKit } from "./kits.js";

let registry: ContentRegistry | null = null;
const kitCache = new Map<string, ClassKit>();

/**
 * Injects the ContentRegistry kits.ts plays against. Node-side callers (tests, or a future server
 * host) pass `loadContent()`'s result once, e.g. `setContentRegistry(loadContent())`; passing null
 * (or never calling this) means every classId is unresolvable and getClassKit() returns null for
 * all of them.
 */
export function setContentRegistry(reg: ContentRegistry | null): void {
  registry = reg;
  kitCache.clear();
}

/** The currently-injected ContentRegistry, or null if setContentRegistry() hasn't been called. */
export function getContentRegistry(): ContentRegistry | null {
  return registry;
}

/**
 * The built ClassKit for `classId`, or null if no registry is injected or `classId` isn't one of
 * content's three classes (e.g. a scripted-test actor's placeholder classId). Callers fall back
 * to BotView.classKit in that case -- see kit-resolve.ts's resolveKit().
 */
export function getClassKit(classId: string): ClassKit | null {
  if (!registry) return null;
  const cached = kitCache.get(classId);
  if (cached) return cached;
  const cls = registry.classes.get(classId);
  if (!cls) return null;
  const kit = buildClassKit(cls, registry.abilities);
  kitCache.set(classId, kit);
  return kit;
}

/**
 * Enemy-ability ids tagged "healer" (BIBLE.md Sec 9 "The Choir": "a 4-second heal cast must be
 * kicked; teaches the kick ledger") -- the priority-cast list shared/ledger.ts honours ahead of
 * any other simultaneous enemy cast. Empty when no registry is injected.
 */
export function priorityInterruptAbilityIds(): string[] {
  if (!registry) return [];
  const ids: string[] = [];
  for (const ability of registry.abilities.values()) {
    if ((ability.tags ?? []).includes(PRIORITY_INTERRUPT_TAG)) ids.push(ability.id);
  }
  return ids.sort();
}
