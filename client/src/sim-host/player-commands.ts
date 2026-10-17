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
import { cycleTarget, faceDir, FACING_Q } from "../../../sim/combat/targeting.js";
import { abilityAffinity } from "../../../sim/combat/affinity.js";
import type { RunSetup } from "./run-setup.js";

/** D10 first-run fix: the target a human cast is sent at. A hostile single-target ability with no
 * live enemy targeted auto-targets the nearest live enemy (squared distance, lowest id on a tie) --
 * before this, `targetId ?? humanId` made a fresh run's Strike hit the Warden himself. Everything
 * else keeps the old rule: the current target, else the caster. */
export function castTargetFor(run: Run, humanId: number, abilityId: string): number {
  const player = run.state.actors[humanId];
  const current = player?.targetId ?? null;
  const def = run.content.abilities[abilityId];
  if (!player || !def || def.shape.kind !== "target" || abilityAffinity(def, run.content.effects) !== "hostile") return current ?? humanId;
  const cur = current !== null ? run.state.actors[current] : undefined;
  if (cur && cur.alive && cur.kind !== player.kind) return cur.id;
  let best: { id: number; d2: number } | null = null;
  for (const a of Object.values(run.state.actors)) {
    if (!a.alive || a.kind === player.kind) continue;
    const d2 = (a.pos.x - player.pos.x) ** 2 + (a.pos.z - player.pos.z) ** 2;
    if (!best || d2 < best.d2 || (d2 === best.d2 && a.id < best.id)) best = { id: a.id, d2 };
  }
  return best?.id ?? current ?? humanId;
}

export interface PendingInput {
  moves: { dx: number; dz: number }[];
  casts: string[];
  targetCycles: number;
  /** Space presses this tick (D10); at most one dash is sent per tick. */
  dashes?: number;
}

/** A dash goes along this tick's move (camera-relative WASD) if there is one, else along the
 * actor's current facing, via the sim's own sim/combat/targeting.ts faceDir (yaw 0 faces +x, 256
 * faces +z -- the yawFromComponents convention), rescaled from Q12 to integer components within
 * sim/core/commands.ts's +/-1000 dir range (tests/client/sim-host/dash-dir.test.ts). */
export function resolveDashDir(move: { dx: number; dz: number } | undefined, facingYaw1024: number): { x: number; z: number } {
  if (move && (move.dx !== 0 || move.dz !== 0)) return { x: move.dx, z: move.dz };
  const face = faceDir(facingYaw1024);
  return { x: Math.round((face.x * 1000) / FACING_Q) || 0, z: Math.round((face.z * 1000) / FACING_Q) || 0 };
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
    const targetId = castTargetFor(run, humanId, abilityId);
    commands.push({ tick, actorId: humanId, kind: "cast", abilityId, targetId });
  }

  return commands;
}
