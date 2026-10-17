// Pure input -> sim command mapping. No DOM/Three.js dependency so it is
// directly unit-testable given a key state and tick.

import { KEYBIND_ACTIONS, type Keybinds } from "./keybinds.js";
import { isAbilityAction, type SlotAbilities } from "./slot-abilities.js";

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
 *
 * A cast carries the ability id in that key's slot, from `slotAbilities` (the host's
 * slot-abilities.ts resolution for this run's class kit). With no map (a host that does not
 * report one), ability keys emit nothing rather than a cast of some other id: fail closed.
 */
export function deriveAbilityCommands(
  codesJustPressed: ReadonlySet<string>,
  keybinds: Keybinds,
  slotAbilities: SlotAbilities | undefined,
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
    if (!isAbilityAction(action)) continue;
    const abilityId = slotAbilities?.[action];
    if (abilityId) commands.push({ type: "cast", abilityId, tick });
  }
  return commands;
}
