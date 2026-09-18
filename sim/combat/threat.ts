/**
 * Per-actor threat table (threat generated against this actor, keyed by source actor id).
 * Amounts are integers; multiplierBp is basis points applied to `amount` before adding.
 * Usage: addThreat(defenderActor, attackerId, 100, 15000) // +150 threat (100 * 150%)
 */
import type { ActorId, ActorState } from "./types";
import { BP_DENOM } from "./effects";

export function addThreat(actor: ActorState, sourceId: ActorId, amount: number, multiplierBp?: number): void {
  const mult = multiplierBp ?? BP_DENOM;
  const delta = Math.trunc((amount * mult) / BP_DENOM);
  actor.threat[sourceId] = (actor.threat[sourceId] ?? 0) + delta;
}

export function highestThreatSource(actor: ActorState): ActorId | null {
  let best: ActorId | null = null;
  let bestVal = -Infinity;
  for (const key of Object.keys(actor.threat)) {
    const id = Number(key);
    const val = actor.threat[id] ?? 0;
    if (val > bestVal) {
      bestVal = val;
      best = id;
    }
  }
  return best;
}

export function clearThreat(actor: ActorState, sourceId: ActorId): void {
  delete actor.threat[sourceId];
}
