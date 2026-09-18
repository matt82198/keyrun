// Usage: replay a full command stream deterministically from a seed. record() snapshots a
// command list into canonical (tick-sorted, defensively copied) form for storage; apply() drives
// step() from tick 0 through the last command tick; goldenHash() is the one-line assertion golden
// fixtures use (see tests/sim/golden/README.md).

import { step, type StepRules } from "./tick";
import { createState, hashState, type SimState } from "./state";
import type { Command } from "./commands";
import type { SimEvent } from "./events";

/** A defensively-copied, tick-sorted snapshot of a command stream, safe to serialise or store. */
export function record(commands: Command[]): Command[] {
  return commands.map((c) => ({ ...c })).sort((a, b) => a.tick - b.tick);
}

/** Replay `commands` from a fresh createState(seed), stepping through every tick they cover. */
export function apply(
  seed: number,
  commands: Command[],
  rules: StepRules,
): { state: SimState; events: SimEvent[] } {
  const ordered = record(commands);
  const maxTick = ordered.reduce((m, c) => Math.max(m, c.tick), -1);

  let state = createState(seed);
  const events: SimEvent[] = [];
  for (let t = 0; t <= maxTick; t++) {
    const tickCommands = ordered.filter((c) => c.tick === t);
    const result = step(state, tickCommands, rules);
    state = result.state;
    events.push(...result.events);
  }
  return { state, events };
}

/** hashState of the final replayed state -- the single value a golden fixture pins. */
export function goldenHash(seed: number, commands: Command[], rules: StepRules): string {
  return hashState(apply(seed, commands, rules).state);
}
