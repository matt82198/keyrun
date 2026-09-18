/**
 * bots/src/shared/windows.ts
 *
 * Finds open Windows (design/BIBLE.md Sec 4) on enemy actors that this bot's class can close
 * with a Payoff -- "Any *other* class's Payoff ability landing inside the ring closes it... A
 * class cannot combo its own Window" (the Lock rule, content/index.ts's canPayoffCloseWindow()).
 *
 * Contract note (no sim/combat/windows.ts exists yet -- see kits.ts's drift note): ActorState's
 * `effects` array (bots/src/types.ts) carries no explicit Window/setupClass field. This module
 * follows the dispatch brief's convention that a Window effect instance's `effectId` is prefixed
 * "window:" (matching bots/src/shared/pings.ts's WINDOW_EFFECT_PREFIX / pings-policy.ts's
 * existing convention) and derives which class opened it from the *existing* `sourceId` field:
 * `sourceId` is the actor that cast the Setup ability, so `actors[sourceId].classId` is the
 * Window's setup class. No new field is invented; the real integration (once sim/combat carries
 * an explicit setupClass) is a drop-in replacement for openWindowsOn().
 *
 * Usage: `import { openWindowsOn, findClosableWindow } from "./windows.js"`
 */
import type { ActorId, ActorState, Tick } from "../types.js";
import { WINDOW_EFFECT_PREFIX } from "./pings.js";

export interface OpenWindow {
  targetId: ActorId;
  windowId: string;
  /** classId of whoever cast the Setup ability that opened this Window; null if unknown (dead/missing source). */
  setupClass: string | null;
  endsTick: Tick;
}

/** design/BIBLE.md Sec 9 "The Lock": a Payoff may close a Window only if its class differs from the class that opened it. */
export function canClose(windowSetupClass: string | null, payoffClass: string): boolean {
  return windowSetupClass === null || windowSetupClass !== payoffClass;
}

/** Every still-open Window effect carried by an alive enemy, regardless of who could close it. */
export function openWindowsOn(actors: Record<ActorId, ActorState>, tick: Tick): OpenWindow[] {
  const out: OpenWindow[] = [];
  for (const actor of Object.values(actors)) {
    if (actor.kind !== "enemy" || !actor.alive) continue;
    for (const effect of actor.effects) {
      if (!effect.effectId.startsWith(WINDOW_EFFECT_PREFIX)) continue;
      if (effect.endsTick <= tick) continue;
      const source = actors[effect.sourceId];
      out.push({
        targetId: actor.id,
        windowId: effect.effectId.slice(WINDOW_EFFECT_PREFIX.length),
        setupClass: source ? source.classId : null,
        endsTick: effect.endsTick,
      });
    }
  }
  return out;
}

/**
 * The best open Window `payoffClass` can close: earliest-expiring first (close it before it's
 * wasted), tie-broken by lowest targetId. Null if no open enemy Window qualifies under the Lock.
 */
export function findClosableWindow(
  actors: Record<ActorId, ActorState>,
  tick: Tick,
  payoffClass: string
): OpenWindow | null {
  const candidates = openWindowsOn(actors, tick).filter((w) => canClose(w.setupClass, payoffClass));
  if (candidates.length === 0) return null;
  candidates.sort((a, b) => (a.endsTick !== b.endsTick ? a.endsTick - b.endsTick : a.targetId - b.targetId));
  return candidates[0] ?? null;
}
