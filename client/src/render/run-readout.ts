/**
 * client/src/render/run-readout.ts
 *
 * Pure model behind render/hud.ts's top-left run readout (D10: readability of every mechanic in 3D).
 * Snapshot state only (D3): which pull the party is on, and one marker per party member.
 *
 * - `formatPullReadout(snapshot)` -> "Pull <n>/<total>" from `Snapshot.pullIndex` / `Snapshot.pullCount`
 *   (both hosts set pullCount = dungeon.pulls.length: sim-host/snapshot-codec.ts and server/src/snapshot.ts).
 *   pullIndex is the sim's EncounterState.pullIndex shown as-is (0 at run start). When pullCount is
 *   absent or not a positive finite number the readout degrades to "Pull <n>".
 * - `partyMarkers(snapshot)` -> one marker per `kind: "player"` actor, ordered by id so markers never
 *   swap between snapshots; hpFraction = health / maxHealth clamped to [0, 1], 0 when dead or maxHealth <= 0.
 *
 * Usage: `hud.updateRunReadout(snapshot)` (render/hud.ts) calls both per snapshot.
 */
import type { Snapshot } from "../sim-host/snapshot.js";

export interface PartyMarker {
  id: number;
  role: string;
  alive: boolean;
  /** health / maxHealth in [0, 1]; 0 for a dead member or maxHealth <= 0. */
  hpFraction: number;
}

/** "Pull 0/4" (index over count), or "Pull 0" when the snapshot carries no usable pull count. */
export function formatPullReadout(snapshot: Pick<Snapshot, "pullIndex" | "pullCount">): string {
  const index = snapshot.pullIndex ?? 0;
  const count = snapshot.pullCount;
  return count !== undefined && Number.isFinite(count) && count > 0 ? `Pull ${index}/${count}` : `Pull ${index}`;
}

/** One marker per party member (player actor), sorted by actor id. */
export function partyMarkers(snapshot: Pick<Snapshot, "actors">): PartyMarker[] {
  return snapshot.actors
    .filter((a) => a.kind === "player")
    .sort((a, b) => a.id - b.id)
    .map((a) => ({
      id: a.id,
      role: a.role,
      alive: a.alive,
      hpFraction: a.alive && a.maxHealth > 0 ? Math.min(1, Math.max(0, a.health / a.maxHealth)) : 0,
    }));
}
