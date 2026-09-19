/**
 * client/src/sim-host/boot-params.ts
 *
 * Resolves main.ts's `?key=&dungeon=&class=` URL params into run-setup.ts's RunSetupOptions,
 * falling back (with a console.warn, never a hard failure) for anything unknown -- unknown/
 * missing `class` defaults to Warden, unknown `dungeon` defaults to Hollow Vault. Split out of
 * main.ts to keep it under the repo's 190-LOC-per-file gate and independently testable.
 *
 * Usage: `const opts = resolveRunSetupOptions(new URLSearchParams(location.search), registry);`
 */
import { resolveHumanClassId } from "./content-party.js";
import type { ContentRegistry } from "../../../content/schema/registry.js";
import type { RunSetupOptions } from "./run-setup.js";

const DEFAULT_KEY_LEVEL = 5;
const DEFAULT_DUNGEON_ID = "hollow-vault";

/** research/11-tick-rate-measurement.md (D9): `?hz=` overrides the client's real-time tick
 * pacing (see worker.ts's SimHost `tickMs`) so the measurement lane can drive the same playable
 * at a different tick rate without editing sim/. `undefined` (param absent/invalid) leaves
 * SimHost on its SIM_TICK_MS default -- unchanged behaviour for every existing URL. */
export function parseHz(raw: string | null): number | undefined {
  if (raw === null || raw.trim() === "") return undefined;
  const n = Number(raw);
  if (!Number.isFinite(n) || n <= 0) return undefined;
  return Math.min(240, Math.max(1, Math.trunc(n)));
}

export function parseKeyLevel(raw: string | null): number {
  // Number(null) and Number("") are both 0 (finite) -- guard the "param absent" case explicitly
  // rather than let it silently clamp to the floor of the 2-20 range.
  if (raw === null || raw.trim() === "") return DEFAULT_KEY_LEVEL;
  const n = Number(raw);
  if (!Number.isFinite(n)) return DEFAULT_KEY_LEVEL;
  return Math.min(20, Math.max(2, Math.trunc(n)));
}

export function resolveRunSetupOptions(url: URLSearchParams, registry: ContentRegistry): RunSetupOptions {
  const keyLevel = parseKeyLevel(url.get("key"));

  const humanClassId = resolveHumanClassId(url.get("class"));
  const rawClass = (url.get("class") ?? "").toLowerCase();
  if (rawClass !== humanClassId) {
    console.warn(`keyrun: "?class=${rawClass || "(none)"}" is not one of warden/cinder/chanter -- defaulting to "${humanClassId}"`);
  }

  const requestedDungeonId = url.get("dungeon") ?? DEFAULT_DUNGEON_ID;
  const dungeonId = registry.dungeons.has(requestedDungeonId) ? requestedDungeonId : DEFAULT_DUNGEON_ID;
  if (dungeonId !== requestedDungeonId) {
    console.warn(`keyrun: unknown dungeon "${requestedDungeonId}" -- defaulting to "${dungeonId}"`);
  }

  return { keyLevel, dungeonId, humanClassId };
}
