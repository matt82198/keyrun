// Pure input -> sim command mapping. No DOM/Three.js dependency so it is
// directly unit-testable given a key state and tick.

import { KEYBIND_ACTIONS, abilityIdForAction, type Keybinds } from "./keybinds.js";

export interface CastCommand {
  type: "cast";
  abilityId: string;
  tick: number;
}

export interface TargetCycleCommand {
  type: "targetCycle";
  tick: number;
}

export interface MoveCommand {
  type: "move";
  dx: number;
  dz: number;
  tick: number;
}

export type AnyCommand = CastCommand | TargetCycleCommand | MoveCommand;

/**
 * Derives ability/target-cycle commands from the set of keyboard codes that
 * were newly pressed this tick (edge-triggered: holding a key does not
 * re-fire it). Order is deterministic, following keybind action order
 * (1,2,3,4,5,Q,E,R,F,Tab) regardless of Set iteration order.
 */
export function deriveAbilityCommands(
  codesJustPressed: ReadonlySet<string>,
  keybinds: Keybinds,
  tick: number,
): AnyCommand[] {
  const commands: AnyCommand[] = [];
  for (const action of KEYBIND_ACTIONS) {
    const code = keybinds[action];
    if (!code || !codesJustPressed.has(code)) continue;
    if (action === "targetCycle") {
      commands.push({ type: "targetCycle", tick });
      continue;
    }
    const abilityId = abilityIdForAction(action);
    if (abilityId) commands.push({ type: "cast", abilityId, tick });
  }
  return commands;
}
