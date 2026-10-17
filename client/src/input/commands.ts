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

/** Space (D10): a one-tick movement burst. Direction is resolved host-side from this tick's move
 * (else current facing) by sim-host/player-commands.ts's resolveDashDir; the sim owns the
 * distance (sim/combat/movement.ts DASH_DISTANCE_MM). */
export interface DashCommand {
  type: "dash";
  tick: number;
}

export type AnyCommand = CastCommand | TargetCycleCommand | MoveCommand | DashCommand;

/** Space is a hardcoded movement control (input/keybinds.ts), not one of the configurable binds. */
export const DASH_CODE = "Space";

/**
 * Derives ability/target-cycle commands from the set of keyboard codes that
 * were newly pressed this tick (edge-triggered: holding a key does not
 * re-fire it). Order is deterministic, following keybind action order
 * (1,2,3,4,5,Q,E,R,F,Tab) regardless of Set iteration order, then a Space dash last.
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
  // Edge-triggered like the binds above: one dash per Space press, none while it is held.
  if (codesJustPressed.has(DASH_CODE)) commands.push({ type: "dash", tick });
  return commands;
}
