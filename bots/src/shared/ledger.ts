/**
 * bots/src/shared/ledger.ts
 *
 * The kick ledger (design/BIBLE.md Sec 9 "The Choir": "a 4-second heal cast must be kicked;
 * teaches the kick ledger"; bots/CLAUDE.md: "Interrupt assignment is deterministic: the bot with
 * the lowest actorId whose kick is ready takes it"). Two independent, pure decisions:
 *
 *  1. Which casting enemy gets kicked, when more than one is casting at once -- ranked by a
 *     priority-cast list (ability ids that must never be allowed to resolve, e.g. a healer cast),
 *     highest priority first; unlisted casts are lowest priority; ties broken by lowest actorId.
 *  2. Who kicks it -- the lowest actorId among every alive player whose OWN interrupt ability is
 *     off cooldown and off the GCD. (Each class has exactly one interrupt slot; a caller passes
 *     the interrupt ability id it wants checked, since different classes use different ids --
 *     see roles.ts, which calls this once per class kit present in the party.)
 *
 * Usage: `import { resolveInterrupt } from "./ledger.js"`
 */
import type { ActorId, ActorState, Tick } from "../types.js";
import { SPELL_QUEUE_WINDOW_TICKS } from "../../../sim/combat/spell-queue.js";

export interface CastingTarget {
  actorId: ActorId;
  abilityId: string;
}

export function castingEnemies(actors: Record<ActorId, ActorState>): CastingTarget[] {
  return Object.values(actors)
    .filter((a): a is ActorState & { castId: string } => a.kind === "enemy" && a.alive && a.castId !== null)
    .map((a) => ({ actorId: a.id, abilityId: a.castId }));
}

/**
 * The casting enemy to kick: the one whose cast ranks highest in `priorityAbilityIds` (index 0 =
 * highest priority); casts not on the list rank below every listed one. Ties (including "none of
 * these casts are on the list") break on lowest actorId. Null if nothing is casting.
 */
export function selectPriorityCastTarget(
  casting: readonly CastingTarget[],
  priorityAbilityIds: readonly string[] = []
): ActorId | null {
  if (casting.length === 0) return null;
  const rank = (abilityId: string): number => {
    const idx = priorityAbilityIds.indexOf(abilityId);
    return idx === -1 ? priorityAbilityIds.length : idx;
  };
  let best: CastingTarget | null = null;
  let bestRank = Number.POSITIVE_INFINITY;
  for (const c of casting) {
    const r = rank(c.abilityId);
    if (best === null || r < bestRank || (r === bestRank && c.actorId < best.actorId)) {
      best = c;
      bestRank = r;
    }
  }
  return best ? best.actorId : null;
}

/** The lowest actorId among the eligible kickers, or null if none are eligible. */
export function selectKicker(eligibleActorIds: readonly ActorId[]): ActorId | null {
  if (eligibleActorIds.length === 0) return null;
  return Math.min(...eligibleActorIds);
}

export interface InterruptAssignment {
  targetId: ActorId;
  kickerId: ActorId;
}

/**
 * Full ledger resolution for one interrupt ability id: which enemy gets kicked and who kicks it.
 * `eligibleClassmates` should already be narrowed to alive players whose own class carries
 * `interruptAbilityId` -- ledger.ts doesn't know class kits, only cooldown readiness -- so a
 * mixed-class party calls this once per distinct interrupt ability id present in the party (see
 * roles.ts's ledgerKickCommand).
 */
/**
 * True when `a` is mid-cast at `tick`: the sim refuses every press then (casting.ts startCast "casting"). A cast ending
 * on `tick` completes before that tick's commands run, so it does not block. Mirrors Readiness.cs IsCasting.
 */
export function isCasting(a: ActorState, tick: Tick): boolean {
  return a.castId !== null && (a.castEndsTick ?? Number.MAX_SAFE_INTEGER) > tick;
}

/**
 * True when `a`'s cast under way still locks it out of a press at `tick`: casting with more than
 * SPELL_QUEUE_WINDOW_TICKS ticks left. Inside the last window ticks the sim queues a press and fires it on the tick
 * the cast ends (sim/combat/spell-queue.ts), so a bot presses there. Mirrors Readiness.cs IsCastLocked.
 */
export function isCastLocked(a: ActorState, tick: Tick): boolean {
  return isCasting(a, tick) && (a.castEndsTick ?? Number.MAX_SAFE_INTEGER) - tick > SPELL_QUEUE_WINDOW_TICKS;
}

/** The tick a press by `a` at `tick` lands as far as its own cast goes: now, or the end of the cast under way. */
export function landingTick(a: ActorState, tick: Tick): Tick {
  return isCasting(a, tick) ? (a.castEndsTick ?? Number.MAX_SAFE_INTEGER) : tick;
}

export function resolveInterrupt(
  actors: Record<ActorId, ActorState>,
  interruptAbilityId: string,
  eligibleClassmates: readonly ActorId[],
  tick: Tick,
  priorityAbilityIds: readonly string[] = [],
  interruptOffGcd = false
): InterruptAssignment | null {
  const targetId = selectPriorityCastTarget(castingEnemies(actors), priorityAbilityIds);
  if (targetId === null) return null;
  // Spell queue window (isCastLocked): a kicker in the last window ticks of its own cast counts, its kick landing when
  // that cast ends, but only when that tick is still inside the enemy's cast. The kick that lands first wins, ties to
  // the lowest id (BIBLE s3: intended tick, fixed tiebreak). Mirrors Ledger.cs ResolveInterrupt.
  const targetEnds = actors[targetId]?.castEndsTick ?? Number.MAX_SAFE_INTEGER;
  const ready = eligibleClassmates.filter((id) => {
    const a = actors[id];
    return a && a.alive && !isCastLocked(a, tick) && landingTick(a, tick) < targetEnds && (a.cooldowns[interruptAbilityId] ?? 0) <= tick && (interruptOffGcd || a.gcdReadyTick <= tick);
  });
  if (ready.length === 0) return null;
  const firstLanding = Math.min(...ready.map((id) => landingTick(actors[id]!, tick)));
  const kickerId = selectKicker(ready.filter((id) => landingTick(actors[id]!, tick) === firstLanding));
  if (kickerId === null) return null;
  return { targetId, kickerId };
}
