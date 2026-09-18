/**
 * bots/src/shared/priority.ts
 *
 * Target-selection and rotation-priority helpers shared by the role state
 * machines in bot.ts. Pure functions over a BotView's actor table; no
 * mutation, no randomness.
 *
 * Usage: `import { pickTankTarget, pickHealTarget, pickRotationAbility } from "./priority.js"`
 */
import type { ActorId, ActorState } from "../types.js";

function aliveEnemies(actors: Record<ActorId, ActorState>): ActorState[] {
  return Object.values(actors).filter((a) => a.kind === "enemy" && a.alive);
}

function aliveAllies(actors: Record<ActorId, ActorState>): ActorState[] {
  return Object.values(actors).filter((a) => a.kind === "player" && a.alive);
}

/** How far below the top threat holder `tankId` currently sits on `enemy`. */
export function threatDeficit(enemy: ActorState, tankId: ActorId): number {
  const values = Object.values(enemy.threat);
  const top = values.length > 0 ? Math.max(...values) : 0;
  const mine = enemy.threat[tankId] ?? 0;
  return Math.max(0, top - mine);
}

/**
 * Tank target selection: the alive enemy with the highest threat deficit
 * (i.e. most off the tank), tie-broken by lowest actorId. Null if no
 * enemies remain.
 */
export function pickTankTarget(actors: Record<ActorId, ActorState>, tankId: ActorId): ActorId | null {
  const enemies = aliveEnemies(actors);
  if (enemies.length === 0) return null;
  let best: ActorState | null = null;
  let bestDeficit = -1;
  for (const e of enemies) {
    const d = threatDeficit(e, tankId);
    if (d > bestDeficit || (d === bestDeficit && best !== null && e.id < best.id)) {
      best = e;
      bestDeficit = d;
    }
  }
  return best ? best.id : null;
}

/**
 * Healer heal-target selection: lowest health% ally strictly under
 * `thresholdPct` (0..1), tie-broken by lowest actorId. Null if no one
 * qualifies.
 */
export function pickHealTarget(
  actors: Record<ActorId, ActorState>,
  thresholdPct = 0.7
): ActorId | null {
  const candidates = aliveAllies(actors).filter((a) => a.health / a.maxHealth < thresholdPct);
  if (candidates.length === 0) return null;
  candidates.sort((a, b) => {
    const pa = a.health / a.maxHealth;
    const pb = b.health / b.maxHealth;
    if (pa !== pb) return pa - pb;
    return a.id - b.id;
  });
  const first = candidates[0];
  return first ? first.id : null;
}

/** Any ally currently carrying an effect whose id is in `dispellableIds`, lowest actorId first. */
export function pickDispelTarget(
  actors: Record<ActorId, ActorState>,
  dispellableIds: ReadonlySet<string>
): ActorId | null {
  const carriers = aliveAllies(actors)
    .filter((a) => a.effects.some((e) => dispellableIds.has(e.effectId)))
    .sort((a, b) => a.id - b.id);
  const first = carriers[0];
  return first ? first.id : null;
}

/** First ability in `rotation` for which `isReady` returns true. Null if none are ready. */
export function pickRotationAbility(
  rotation: string[],
  isReady: (abilityId: string) => boolean
): string | null {
  for (const abilityId of rotation) {
    if (isReady(abilityId)) return abilityId;
  }
  return null;
}

/**
 * Interrupt-assignment rule: among `eligibleActorIds` (damage bots with
 * their interrupt ready), only the lowest actorId is responsible. Returns
 * true if `selfId` is the one that should cast the interrupt.
 */
export function isInterruptResponsible(selfId: ActorId, eligibleActorIds: ActorId[]): boolean {
  if (eligibleActorIds.length === 0) return false;
  return selfId === Math.min(...eligibleActorIds);
}
