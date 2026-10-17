/**
 * client/src/render/payoff-ready.ts
 *
 * Window readability (D10): which of YOUR abilities would close an open Window right now. Pure
 * (no DOM, no Three.js). A live enemy's snapshot Window (ActorSnapshot.window, open while
 * tick < endsTick) is closable by you when content has a burst combo for
 * "<window.setupClass>><your classId>" (sim/combat/payoff.ts looks the pair up the same way, and a
 * class never has a combo with itself, so your own setups never light). The live abilities are the
 * `payoff`-flagged ones (sim-host/display-info.ts, from `{op:"payoff"}`) on YOUR bar. Your current target
 * wins when several enemies qualify; otherwise the lowest enemy id.
 *
 * Not modelled: the Lock's alternation rule (the client cannot see which class closed the last Lock).
 * With no display info (the ?net=1 server host) nothing lights.
 *
 * Usage: `const open = openPayoff(snapshot.actors, humanActorId, snapshot.tick, display, slotAbilities);`
 */
import type { ActorSnapshot } from "../sim-host/snapshot.js";
import type { DisplayInfo } from "../sim-host/display-info.js";
import type { SlotAbilities } from "../input/slot-abilities.js";

export interface OpenPayoff {
  /** Your payoff ability ids (pressing one would close the Window). */
  abilityIds: ReadonlySet<string>;
  /** The combo it would fire, e.g. "Shatter". */
  comboName: string;
  /** The enemy carrying the Window. */
  enemyId: number;
}

export function openPayoff(
  actors: readonly ActorSnapshot[],
  humanActorId: number,
  tick: number,
  info: DisplayInfo | undefined,
  slotAbilities: SlotAbilities | undefined,
): OpenPayoff | null {
  const me = actors.find((a) => a.id === humanActorId);
  if (!me || !info?.combos || !slotAbilities) return null;
  const abilityIds = new Set(Object.values(slotAbilities).filter((id) => info.abilities[id]?.payoff === true));
  if (abilityIds.size === 0) return null;
  let best: OpenPayoff | null = null;
  for (const a of actors) {
    if (a.kind !== "enemy" || !a.alive || !a.window || tick >= a.window.endsTick) continue;
    const comboName = info.combos[`${a.window.setupClass}>${me.classId}`];
    if (comboName === undefined) continue;
    const candidate = { abilityIds, comboName, enemyId: a.id };
    if (a.id === me.targetId) return candidate;
    if (!best || a.id < best.enemyId) best = candidate;
  }
  return best;
}
