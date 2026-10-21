/**
 * The spell queue window (PR #370; twin of game/sim/Keyrun.Sim/Combat/SpellQueue.cs). A player's ability is never
 * usable mid-cast (casting.ts startCast "casting") nor, unless its own gcdTicks is 0, during the GCD ("not_ready").
 * WoW softens both with a queue: a press made inside the last SPELL_QUEUE_WINDOW_TICKS ticks of the lock that refused
 * it is held on the actor (ActorState.queuedCast) and fires on the first tick the lock is over. One press is held per
 * actor and the latest wins; a press earlier than the window is dropped, as before. Enemies never queue.
 *
 * Firing: on a tick the lock is over and the actor sent no cast command of its own, the held press runs as that
 * actor's cast command in the tick's fixed (actorId, kind, insertion) order (tick.ts sortCommands). So a queued kick
 * lands on the tick it fires and races any other kick on that tick by actor id, never by when its key went down
 * (BIBLE s3). A fresh cast command on the firing tick supersedes the held press. A held press that then fails is
 * dropped, never retried. The field is deleted (not nulled) when clear, so hashState of a run that never queues is
 * unchanged.
 *
 * Usage: const run = withFiring(state, commands, abilities); for (const cmd of run.commands) { ...
 *   afterPress(actor, def, cmd, err, run.fired.has(cmd), state.tick) }
 */
import type { ActorState, Command, SimState, Tick } from "./types";
import type { AbilityDef } from "./abilities";
import { TICK_MS, sortCommands } from "../core/tick";

/** WoW's default spell queue window, 400 ms, in ticks (8 at 20 Hz). Mirrors SpellQueue.WindowTicks. */
export const SPELL_QUEUE_WINDOW_TICKS = Math.round(400 / TICK_MS);

type CastCmd = Extract<Command, { kind: "cast" }>;

/** The first tick the cast under way and (unless def is off the GCD) the GCD no longer block a press of `def`. */
export function lockEndsTick(actor: ActorState, def: AbilityDef, now: Tick): Tick {
  let end = now;
  if (actor.castId !== null) end = Math.max(end, actor.castEndsTick ?? Number.MAX_SAFE_INTEGER);
  if (def.gcdTicks > 0) end = Math.max(end, actor.gcdReadyTick);
  return end;
}

/** True when a press refused with `error` is queued: a living player, refused by its cast or GCD, inside the window. */
export function queues(actor: ActorState, def: AbilityDef, error: string, now: Tick): boolean {
  if (actor.kind !== "player" || !actor.alive || (error !== "casting" && error !== "not_ready")) return false;
  const wait = lockEndsTick(actor, def, now) - now;
  return wait > 0 && wait <= SPELL_QUEUE_WINDOW_TICKS;
}

/**
 * This tick's commands plus each held press that fires now (alive actor, no cast command of its own this tick, lock
 * over) as its actor's cast command, re-sorted. A dead actor's or unknown ability's held press is dropped.
 */
export function withFiring(
  state: SimState,
  commands: Command[],
  abilities: Record<string, AbilityDef>,
): { commands: Command[]; fired: Set<Command> } {
  const fired = new Set<Command>();
  let merged: Command[] | null = null;
  for (const actor of Object.values(state.actors)) {
    const q = actor.queuedCast;
    if (!q) continue;
    const def = abilities[q.abilityId];
    if (!actor.alive || !def) {
      delete actor.queuedCast;
      continue;
    }
    if (commands.some((c) => c.kind === "cast" && c.actorId === actor.id)) continue;
    if (lockEndsTick(actor, def, state.tick) > state.tick) continue;
    const cmd: Command = {
      tick: state.tick,
      actorId: actor.id,
      kind: "cast",
      abilityId: q.abilityId,
      ...(q.targetId !== undefined ? { targetId: q.targetId } : {}),
      ...(q.point !== undefined ? { point: q.point } : {}),
    };
    merged ??= [...commands];
    merged.push(cmd);
    fired.add(cmd);
  }
  return { commands: merged === null ? commands : sortCommands(merged), fired };
}

/**
 * Updates the held press after a cast command ran with startCast's `error`: a started cast or a fired held press
 * clears it; a fresh press inside the window replaces it (latest wins); a fresh press refused once its lock is over
 * supersedes and clears it; a fresh press earlier than the window leaves it held.
 */
export function afterPress(actor: ActorState, def: AbilityDef, cmd: CastCmd, error: string | null, fromQueue: boolean, now: Tick): void {
  if (error === null || fromQueue) {
    delete actor.queuedCast;
  } else if (queues(actor, def, error, now)) {
    actor.queuedCast = {
      abilityId: cmd.abilityId,
      ...(cmd.targetId !== undefined ? { targetId: cmd.targetId } : {}),
      ...(cmd.point !== undefined ? { point: cmd.point } : {}),
      pressTick: now,
    };
  } else if (lockEndsTick(actor, def, now) <= now) {
    delete actor.queuedCast;
  }
}
