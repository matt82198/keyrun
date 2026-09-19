/**
 * tests/bots/apply-move.ts
 *
 * Shared test helper: applies an integer-valid `move` Command's unit-vector `dir` (an integer
 * scaled to +-1000, sim/core/commands.ts's validated range) to a hand-rolled `{x, z}` mm position
 * by `stepMM` millimetres this tick -- the same math sim/combat/movement.ts's real applyMovement
 * performs (`Math.trunc((dir * speed) / 1000)`), reused here so a bots/** unit test that hand-steps
 * an actor (rather than driving it through sim/core/tick.ts's real step()) still moves it exactly
 * the way the real sim would, instead of misreading `dir` as a float unit vector in [-1, 1].
 *
 * Usage: `pos = applyMove(pos, move, STEP_MM);`
 */
import type { Command } from "@bots/src/types.js";

export function applyMove(pos: { x: number; z: number }, cmd: Command | undefined, stepMM: number): { x: number; z: number } {
  if (!cmd || cmd.kind !== "move") return pos;
  return {
    x: pos.x + Math.trunc((cmd.dir.x * stepMM) / 1000),
    z: pos.z + Math.trunc((cmd.dir.z * stepMM) / 1000),
  };
}
