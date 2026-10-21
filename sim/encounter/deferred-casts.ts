/**
 * sim/encounter/deferred-casts.ts -- STARTCAST-BUSY: scripted casts held for a busy caster.
 *
 * sim/combat/casting.ts's startCast refuses a caster with a cast in flight (BIBLE s3: a cast bar is never
 * silently replaced). A scripted step that fires while its caster is busy (a cast in flight, or a cast-time cast
 * this same stepEnemyScripts call already issued for it) is therefore HELD here and issued the first tick the
 * caster is free, instead of being sent to combat and dropped. That keeps the boss's core rule happening (BIBLE
 * s9: the unarmed hollowWarden.lock fires on the slam's tick and now lands when the slam ends).
 *
 * Rules: held casts go first (oldest first), then the tick's own steps; a held step is held once (a re-fire
 * while held collapses into it); it is dropped if its caster dies or leaves the scripts map; and it re-draws its
 * telegraph on the tick it is issued (the onDeath convention in scripts.ts), so the cast it finally starts is
 * still drawn ahead of its resolution.
 *
 * Usage (scripts.ts): `const fire = casterGate(actorId, actor, tick, abilities, held, nextHeld, issue);`
 * then `fire(step)` for every step that fires this tick.
 */
import type { ActorId, ActorState, Tick } from "./types";
import type { AbilityDef } from "../combat/abilities";
import type { ScriptStep } from "./scripts";

export interface DeferredCast {
  actorId: ActorId;
  step: ScriptStep;
}

/** rules.ts completes a cast whose castEndsTick is this tick BEFORE it runs commands, so that caster is free. */
export function busyAt(actor: ActorState, tick: Tick): boolean {
  return actor.castId !== null && actor.castEndsTick !== tick;
}

/** An issued cast keeps its caster busy for the rest of this tick only if it has a cast time (an instant
 * completes inside rules.ts's command loop). An ability the registry does not know never starts at all. */
export function leavesCastInFlight(cast: string, abilities: Record<string, AbilityDef>): boolean {
  const def = abilities[cast];
  return def !== undefined && def.castTicks > 0;
}

/**
 * One caster's gate for one tick. Issues (or re-holds) this caster's held casts into `nextHeld` immediately and
 * returns `fire(step)` for the tick's own steps. `issue(step, redraw)` builds and pushes the cast command (and,
 * when `redraw`, the step's telegraph) and returns false when target resolution blocked it.
 */
export function casterGate(
  actorId: ActorId,
  actor: ActorState,
  tick: Tick,
  abilities: Record<string, AbilityDef>,
  held: readonly DeferredCast[],
  nextHeld: DeferredCast[],
  issue: (step: ScriptStep, redraw: boolean) => boolean,
): (step: ScriptStep) => void {
  let claimed = actor.alive && busyAt(actor, tick);
  const fire = (step: ScriptStep, redraw: boolean): void => {
    if (claimed) {
      if (!nextHeld.some((d) => d.actorId === actorId && d.step.cast === step.cast)) nextHeld.push({ actorId, step });
      return;
    }
    if (issue(step, redraw)) claimed = leavesCastInFlight(step.cast, abilities);
  };
  if (actor.alive) {
    for (const h of held) {
      if (h.actorId === actorId) fire(h.step, true);
    }
  }
  return (step) => fire(step, false);
}
