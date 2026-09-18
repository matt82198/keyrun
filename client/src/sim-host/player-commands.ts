/**
 * client/src/sim-host/player-commands.ts
 *
 * Turns the human player's queued input (this tick's move deltas, ability casts, target-cycle
 * presses) into sim Commands addressed to `setup.humanActorId`. Split out of worker.ts to keep it
 * under the repo's 190-LOC-per-file gate.
 *
 * Usage: `const commands = computePlayerCommands(run, setup, tick, pending);`
 */
import type { Run } from "../../../sim/index.js";
import type { Tick } from "../../../sim/core/state.js";
import type { Command } from "../../../sim/core/commands.js";
import { yawFromComponents } from "../../../sim/core/atan.js";
import { cycleTarget } from "../../../sim/combat/targeting.js";
import type { RunSetup } from "./run-setup.js";

export interface PendingInput {
  moves: { dx: number; dz: number }[];
  casts: string[];
  targetCycles: number;
}

export function computePlayerCommands(run: Run, setup: RunSetup, tick: Tick, pending: PendingInput): Command[] {
  const commands: Command[] = [];
  const humanId = setup.humanActorId;
  const player = run.state.actors[humanId];

  for (const move of pending.moves) {
    if (move.dx === 0 && move.dz === 0) continue;
    const dir = { x: move.dx, z: move.dz };
    commands.push({ tick, actorId: humanId, kind: "move", dir, yaw: yawFromComponents(dir.x, dir.z) });
  }

  if (pending.targetCycles > 0 && player) {
    const enemies = Object.values(run.state.actors).filter((a) => a.kind === "enemy");
    let targetId = player.targetId;
    for (let i = 0; i < pending.targetCycles; i++) targetId = cycleTarget(enemies, targetId);
    commands.push({ tick, actorId: humanId, kind: "target", targetId });
  }

  for (const abilityId of pending.casts) {
    const targetId = player?.targetId ?? humanId;
    commands.push({ tick, actorId: humanId, kind: "cast", abilityId, targetId });
  }

  return commands;
}
