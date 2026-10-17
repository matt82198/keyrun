/**
 * client/src/sim-host/player-commands.ts
 *
 * Turns the human player's queued input (this tick's move deltas, Space dash, ability casts,
 * target-cycle presses) into sim Commands addressed to `setup.humanActorId`. Split out of worker.ts to keep it
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
  /** Space presses this tick (D10); at most one dash is sent per tick. */
  dashes?: number;
}

/** A dash goes along this tick's move (camera-relative WASD) if there is one, else along the
 * actor's current facing (sim yaw in 1/1024 turns: 0 faces +z, 256 faces +x). Integer components
 * within sim/core/commands.ts's +/-1000 dir range. */
export function resolveDashDir(move: { dx: number; dz: number } | undefined, facingYaw1024: number): { x: number; z: number } {
  if (move && (move.dx !== 0 || move.dz !== 0)) return { x: move.dx, z: move.dz };
  const rad = (facingYaw1024 / 1024) * Math.PI * 2;
  return { x: Math.round(Math.sin(rad) * 1000) || 0, z: Math.round(Math.cos(rad) * 1000) || 0 };
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

  if ((pending.dashes ?? 0) > 0 && player) {
    const dir = resolveDashDir(pending.moves[pending.moves.length - 1], player.yaw);
    commands.push({ tick, actorId: humanId, kind: "dash", dir, yaw: yawFromComponents(dir.x, dir.z) });
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
