/**
 * client/src/net/mapping.ts
 *
 * Pure translation helpers between the server's wire shapes (wire-types.ts, mirrored from
 * server/src/protocol.ts + snapshot.ts) and this client's local host-interface shapes
 * (client/src/sim-host/snapshot.ts's Snapshot, sim-host/wire.ts's HostCommand) -- so net-client.ts
 * hands hud-wiring.ts/render code the exact same Snapshot shape the local Worker host already
 * produces, and sends the server the exact sim/core/commands.ts Command payload
 * sim-host/player-commands.ts derives for the local path (same yawFromComponents call, same
 * targetId-defaults-to-current-target-or-self rule).
 *
 * P2 (policy-inside-stepRun): `toClientSnapshot` no longer derives a `runEnd` signal from wire
 * snapshot data (the old `isRunEnded`/`runOutcome`, deleted) -- the server now sends an explicit,
 * authoritative `run_end` message (server/src/protocol.ts) the moment sim/index.ts's stepRun
 * decides the run is over; net-client.ts consumes that directly and overlays it onto the mapped
 * Snapshot, the same one-authority contract sim-runtime.ts's local path now follows.
 *
 * Usage: `const snapshot = toClientSnapshot(serverSnapshot);`
 *        `const payload = translateHostCommand(cmd, { targetId, enemies });`
 */
import { yawFromComponents } from "../../../sim/core/atan.js";
import type { HostCommand } from "../sim-host/wire.js";
import { MAX_SNAPSHOT_EVENTS, type ActorSnapshot as ClientActorSnapshot, type Snapshot as ClientSnapshot, type SnapshotEvent } from "../sim-host/snapshot.js";
import type { ClientCommandPayload, PartyRole, ServerActorSnapshot, ServerSnapshot } from "./wire-types.js";

/** Mirrors server/src/roster.ts's ROLE_ROSTER class -> role binding (read-only, not imported --
 * server/** is off-limits to this lane). Only three classes exist (content/data/classes), each
 * bound to exactly one role, same simplification client/src/sim-host/content-party.ts makes. */
const CLASS_ROLE: Record<string, PartyRole> = { warden: "tank", chanter: "healer", cinder: "damage" };
const DEFAULT_CLASS_ID = "warden";

export function resolveClassId(param: string | null): string {
  const normalized = (param ?? "").toLowerCase();
  return normalized in CLASS_ROLE ? normalized : DEFAULT_CLASS_ID;
}

export function roleForClassId(classId: string): PartyRole {
  return CLASS_ROLE[classId] ?? "tank";
}

function toClientActor(a: ServerActorSnapshot): ClientActorSnapshot {
  // Drops the server-only `controlledBy` field -- the client host interface has no equivalent.
  return { id: a.id, kind: a.kind, role: a.role, classId: a.classId, pos: a.pos, yaw: a.yaw, health: a.health, maxHealth: a.maxHealth, resource: a.resource, maxResource: a.maxResource, alive: a.alive, targetId: a.targetId, cast: a.cast, cooldowns: a.cooldowns };
}

/** Untrusted wire input: only well-formed interrupt events reach the renderer's InterruptLatch. */
function isInterruptEvent(e: unknown): e is SnapshotEvent {
  if (typeof e !== "object" || e === null) return false;
  const r = e as Record<string, unknown>;
  return r.type === "interrupt" && typeof r.tick === "number" && typeof r.actorId === "number" && typeof r.sourceId === "number" && typeof r.school === "string";
}

/** Strips server-only fields (stateHash, per-actor controlledBy) -- the exact Snapshot shape
 * hud-wiring.ts already consumes from the local Worker host, minus `runEnd`: net-client.ts overlays
 * that itself from the server's own `run_end` message (see file docstring), never derived here.
 * D10 net parity: ActorCast.kickable, each telegraph's telegraphClass, and the one-shot `events`
 * channel are carried through unchanged, so ?net=1 renders kickable casts, kick flashes and
 * telegraph classes exactly like the local host (a missing `events` maps to none, never stale). */
export function toClientSnapshot(s: ServerSnapshot): ClientSnapshot {
  return {
    tick: s.tick,
    actors: s.actors.map(toClientActor),
    telegraphs: s.telegraphs,
    events: Array.isArray(s.events) ? s.events.filter(isInterruptEvent).slice(0, MAX_SNAPSHOT_EVENTS) : [],
    runTimerTicks: s.runTimerTicks,
    timerLimitTicks: s.timerLimitTicks,
    slackTicks: s.slackTicks,
    maxSlackTicks: s.maxSlackTicks,
    depleted: s.depleted,
    parTicks: s.parTicks,
    pullIndex: s.pullIndex,
    pullCount: s.pullCount,
    runEnd: null,
  };
}

/** Next alive enemy id after `currentTargetId`, cycling by ascending id; the first alive enemy if
 * nothing is targeted yet; null if nothing is alive to target. Content-agnostic stand-in for
 * sim/combat/targeting.ts's cycleTarget (unavailable here -- the net path has no live Run state,
 * only the last snapshot's actor list). */
export function cycleEnemyTarget(enemies: readonly { id: number; alive: boolean }[], currentTargetId: number | null): number | null {
  const alive = enemies
    .filter((e) => e.alive)
    .map((e) => e.id)
    .sort((a, b) => a - b);
  if (alive.length === 0) return null;
  if (currentTargetId === null) return alive[0] ?? null;
  const idx = alive.indexOf(currentTargetId);
  return idx < 0 ? (alive[0] ?? null) : (alive[(idx + 1) % alive.length] ?? null);
}

export interface TargetContext {
  /** The human actor's own id -- sim-host/player-commands.ts's default cast target when nothing
   * else is targeted (self-cast, e.g. a healer with no target healing themself). */
  selfId: number;
  targetId: number | null;
  enemies: readonly { id: number; alive: boolean }[];
}

/** Translates one host-interface command into the wire `cmd` payload sim/core/commands.ts's
 * validateCommand accepts server-side. Pings and "abandon" have no sim-side wire equivalent (see
 * sim-host/wire.ts's PingHostCommand docstring and net-client.ts's own abandon handling) and never
 * cross the wire -- returns null for those. */
export function translateHostCommand(cmd: HostCommand, ctx: TargetContext): ClientCommandPayload | null {
  switch (cmd.type) {
    case "move": {
      const dir = { x: cmd.dx, z: cmd.dz };
      return { tick: cmd.tick, kind: "move", dir, yaw: yawFromComponents(dir.x, dir.z) };
    }
    case "cast":
      return { tick: cmd.tick, kind: "cast", abilityId: cmd.abilityId, targetId: ctx.targetId ?? ctx.selfId };
    case "targetCycle":
      return { tick: cmd.tick, kind: "target", targetId: cycleEnemyTarget(ctx.enemies, ctx.targetId) };
    case "stopcast":
      return { tick: cmd.tick, kind: "stopcast" };
    default:
      return null;
  }
}
