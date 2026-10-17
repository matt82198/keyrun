/**
 * client/src/sim-host/display-info.ts
 *
 * First-run readability (D10): the human-facing names and timings the HUD and the 3D labels need,
 * built ONCE worker-side from the content registry (the main thread never loads content) and
 * shipped in wire.ts's `ready` message. The ability bar names each slot from `abilities`, measures
 * its cooldown sweep against `cooldownTicks`, and dims GCD-bound slots by `gcdTicks`; actor name
 * tags read `actorNames` (keyed by ActorSnapshot.classId: enemy def ids and class ids).
 *
 * `humanizeId` is the fallback when a host reports no display info (the ?net=1 server path):
 * "warden.guardBreak" -> "Guard Break". Display only; it never decides anything.
 *
 * Usage: `const info = buildDisplayInfo(registry, setup.content.abilities);`
 */
import type { ContentRegistry } from "../../../content/schema/registry.js";

export interface AbilityDisplay {
  name: string;
  cooldownTicks: number;
  gcdTicks: number;
  castTicks: number;
}

export interface DisplayInfo {
  abilities: Record<string, AbilityDisplay>;
  actorNames: Record<string, string>;
}

/** Last dotted segment of an id, camelCase split, words capitalised; a trailing ".boss" is dropped. */
export function humanizeId(id: string): string {
  const base = id.replace(/\.boss$/, "").split(".").pop() ?? id;
  return base
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .split(/[\s_-]+/)
    .filter((w) => w.length > 0)
    .map((w) => w[0]!.toUpperCase() + w.slice(1))
    .join(" ");
}

interface AbilityTimings {
  name?: unknown;
  cooldownTicks?: unknown;
  gcdTicks?: unknown;
  castTicks?: unknown;
}

function num(v: unknown): number {
  return typeof v === "number" && Number.isFinite(v) ? v : 0;
}

/** `runAbilities` is the run's RESOLVED ability table (loadout/talent patches applied), so a talent
 * that changes a cooldown or GCD is reflected; names fall back to the registry, then the id. */
export function buildDisplayInfo(registry: ContentRegistry, runAbilities: Readonly<Record<string, AbilityTimings>>): DisplayInfo {
  const abilities: Record<string, AbilityDisplay> = {};
  for (const [id, def] of Object.entries(runAbilities)) {
    const name = typeof def.name === "string" && def.name.length > 0 ? def.name : registry.abilities.get(id)?.name ?? humanizeId(id);
    abilities[id] = { name, cooldownTicks: num(def.cooldownTicks), gcdTicks: num(def.gcdTicks), castTicks: num(def.castTicks) };
  }
  const actorNames: Record<string, string> = {};
  for (const [id, def] of registry.enemies) actorNames[id] = def.name;
  for (const [id, def] of registry.classes) actorNames[id] = def.name;
  return { abilities, actorNames };
}

export function abilityName(info: DisplayInfo | undefined, abilityId: string): string {
  return info?.abilities[abilityId]?.name ?? humanizeId(abilityId);
}

export function actorName(info: DisplayInfo | undefined, classId: string): string {
  return info?.actorNames[classId] ?? humanizeId(classId);
}
