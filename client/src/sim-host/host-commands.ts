/**
 * client/src/sim-host/host-commands.ts
 *
 * Two host-level controls the dispatch brief calls for -- stopcast and "abandoned via a key" --
 * that have no home in client/src/input/ (that directory is out of this lane's ownership; see
 * client/CLAUDE.md and this lane's dispatch brief). Mirrors input/commands.ts's pure,
 * DOM-free "key state in, command out" shape so it is unit-testable the same way, and is wired
 * directly in main.ts alongside the already-hardcoded WASD/Space movement keys (main.ts already
 * hardcodes movement outside the configurable keybinds.json set, so this follows the same
 * established pattern rather than inventing a new one).
 *
 * Neither is a sim Command: "stopcast" maps 1:1 to sim/core/commands.ts's `{kind:"stopcast"}`
 * (worker.ts does that translation); "abandon" has no sim equivalent at all -- it only tells
 * worker.ts to call sim/encounter/timer.ts's `finishRun(..., completed=false, ...)` and end the
 * run locally, never entering `state.tick`'s command stream.
 *
 * Usage: `const controls = deriveHostControlCommands(keyState.sampleAndClearJustPressed(), tick);`
 */

export const STOPCAST_KEY_CODE = "Escape";
export const ABANDON_KEY_CODE = "Backspace";

export type HostControlKind = "stopcast" | "abandon";

export interface HostControlCommand {
  type: HostControlKind;
  tick: number;
}

/** Edge-triggered like input/commands.ts's deriveAbilityCommands: `codesJustPressed` must be the
 * newly-pressed-this-tick set (e.g. KeyState.sampleAndClearJustPressed()), never the held set. */
export function deriveHostControlCommands(codesJustPressed: ReadonlySet<string>, tick: number): HostControlCommand[] {
  const out: HostControlCommand[] = [];
  if (codesJustPressed.has(STOPCAST_KEY_CODE)) out.push({ type: "stopcast", tick });
  if (codesJustPressed.has(ABANDON_KEY_CODE)) out.push({ type: "abandon", tick });
  return out;
}
