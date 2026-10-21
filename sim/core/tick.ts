// Usage: step() advances a SimState by exactly one tick: validate + deterministically order this
// tick's commands, hand them to the combat lane's StepRules to mutate a clone, then advance the
// clock. TICK_HZ is the single source of truth for tick rate; TICK_MS is derived, never literal.

import { validateCommand, type Command } from "./commands";
import { cloneState, type SimState } from "./state";
import type { SimEvent } from "./events";

/** Simulation tick rate. Change this one constant to retune the whole sim (see research/07). */
export const TICK_HZ = 20;
/** Milliseconds per tick, derived from TICK_HZ -- never hardcode 50 elsewhere. */
export const TICK_MS = Math.round(1000 / TICK_HZ);

export type StepRules = (state: SimState, commands: Command[], emit: (e: SimEvent) => void) => void;

// dash (additive, D10) sorts right after move; the existing kinds keep their relative order.
const KIND_ORDER: Record<Command["kind"], number> = { cast: 0, move: 1, dash: 2, stopcast: 3, target: 4 };

/** Deterministic ordering: by actorId ascending, then kind, then original insertion order (also spell-queue.ts). */
export function sortCommands(commands: Command[]): Command[] {
  return commands
    .map((c, i) => ({ c, i }))
    .sort((a, b) => {
      if (a.c.actorId !== b.c.actorId) return a.c.actorId - b.c.actorId;
      const ak = KIND_ORDER[a.c.kind];
      const bk = KIND_ORDER[b.c.kind];
      if (ak !== bk) return ak - bk;
      return a.i - b.i;
    })
    .map((x) => x.c);
}

/**
 * Advance `state` by one tick. Never mutates `state`: works on a clone, which `rules` mutates
 * in place while emitting events. Commands not addressed to the current tick, or that fail
 * validateCommand, are silently dropped (fail closed rather than throw on untrusted input).
 */
export function step(
  state: SimState,
  commands: Command[],
  rules: StepRules,
): { state: SimState; events: SimEvent[] } {
  const events: SimEvent[] = [];
  const valid = commands
    .map((c) => validateCommand(c))
    .filter((c): c is Command => c !== null)
    .filter((c) => c.tick === state.tick);
  const sorted = sortCommands(valid);

  const next = cloneState(state);
  rules(next, sorted, (e) => events.push(e));
  next.tick = next.tick + 1;
  next.runTimerTicks = next.runTimerTicks + 1;

  return { state: next, events };
}
