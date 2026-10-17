/**
 * client/src/sim-host/cast-events.ts
 *
 * D10 projections: turns one stepRun's own SimEvents into snapshot.ts's one-shot CastSnapshotEvents,
 * so render/cast-fx.ts can draw every completed cast's shape and flash whoever it hit. Read-only over
 * sim output: sim/combat/casting.ts's completeCast emits "cast_complete" (actorId, abilityId) AFTER
 * that cast's own "damage"/"heal" events (sourceId, targetId) in the same tick, so a cast's hits are
 * the damage/heal events from the same source since that source's previous cast_complete.
 * Geometry is the ability definition's shape read from the run's content, with the caster's position
 * and facing as they stand after the step (the same state completeCast resolved against).
 *
 * Usage: `const events = castEvents(run, stepped.events, room);` (room = MAX_SNAPSHOT_EVENTS left)
 */
import type { SimEvent } from "../../../sim/core/events.js";
import type { Run } from "../../../sim/index.js";
import type { CastSnapshotEvent } from "./snapshot.js";

function idOf(v: unknown): number | null {
  return typeof v === "number" ? v : null;
}

export function castEvents(run: Run, simEvents: readonly SimEvent[] | undefined, room: number): CastSnapshotEvent[] {
  if (!simEvents || room <= 0) return [];
  const out: CastSnapshotEvent[] = [];
  const pendingHits = new Map<number, number[]>();
  for (const e of simEvents) {
    if (e.type === "damage" || e.type === "heal") {
      const src = idOf(e.sourceId);
      const tgt = idOf(e.targetId);
      if (src === null || tgt === null) continue;
      const list = pendingHits.get(src) ?? [];
      if (!list.includes(tgt)) list.push(tgt);
      pendingHits.set(src, list);
      continue;
    }
    if (e.type !== "cast_complete") continue;
    const actorId = idOf(e.actorId);
    const abilityId = typeof e.abilityId === "string" ? e.abilityId : null;
    if (actorId === null || abilityId === null) continue;
    const caster = run.state.actors[actorId];
    const def = run.content.abilities[abilityId];
    const hitIds = pendingHits.get(actorId) ?? [];
    pendingHits.delete(actorId);
    if (!caster || !def) continue;
    const shape = def.shape;
    out.push({
      type: "cast",
      tick: e.tick,
      actorId,
      abilityId,
      shape: shape.kind,
      origin: { x: caster.pos.x, z: caster.pos.z },
      yaw: caster.yaw,
      ...(shape.radius !== undefined ? { radius: shape.radius } : {}),
      ...(shape.angle1024 !== undefined ? { angle1024: shape.angle1024 } : {}),
      ...(shape.length !== undefined ? { length: shape.length } : {}),
      targetId: caster.targetId,
      hitIds,
    });
    if (out.length >= room) break;
  }
  return out;
}
