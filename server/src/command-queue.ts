/**
 * server/src/command-queue.ts
 *
 * Validates one inbound `cmd` payload (protocol.ts's `{type:"cmd", cmd: unknown}`) against
 * sim/core/commands.ts's real `validateCommand` -- after forcing `actorId` to the sender's bound
 * actor (server/CLAUDE.md: never trust a client-supplied actorId) -- and re-stamps a stale-tick
 * command to `currentTick` rather than dropping it (server-level policy on top of sim/core/tick.ts's
 * own silent-drop). Pulled out of room.ts to keep that file under the repo's 190-LOC cap; pure
 * (no `this.pending` mutation) so room.ts owns exactly one place that pushes to the queue.
 *
 * Usage: `const result = resolveCommand(rawCmd, actorId, this.run.state.tick);
 *         if (result.ok) this.pending.push(result.cmd);`
 */
import { validateCommand, type Command } from "../../sim/core/commands";
import type { ActorId, Tick } from "../../sim/core/state";

export interface EnqueueOk {
  ok: true;
  cmd: Command;
  late?: { originalTick: number; newTick: number };
}
export interface EnqueueErr {
  ok: false;
  reason: string;
}

function isRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

export function resolveCommand(rawCmd: unknown, actorId: ActorId, currentTick: Tick): EnqueueOk | EnqueueErr {
  if (!isRecord(rawCmd)) return { ok: false, reason: "malformed_command" };
  const cmd = validateCommand({ ...rawCmd, actorId });
  if (!cmd) return { ok: false, reason: "malformed_command" };

  if (cmd.tick < currentTick) {
    return { ok: true, cmd: { ...cmd, tick: currentTick }, late: { originalTick: cmd.tick, newTick: currentTick } };
  }
  return { ok: true, cmd };
}
