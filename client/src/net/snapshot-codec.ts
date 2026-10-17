/**
 * client/src/net/snapshot-codec.ts
 *
 * Wire-side decoders for the readability fields the ?net=1 server ships (server/src/snapshot.ts,
 * built with client/src/sim-host/snapshot-codec.ts's own rules). Every input is untrusted network
 * data: each decoder keeps only well-formed entries and never throws, so net/mapping.ts hands the
 * renderer the exact shapes the local worker host produces.
 *  - `decodeEffects`: item-5 status channel ({ id, ticks > 0 }), at most MAX_ACTOR_EFFECTS.
 *  - `decodeEvents`: one-shot events -- landed kicks, completed casts, and `cast_failed` for THIS
 *    client's own actor only (the server broadcasts one snapshot to every human in the room).
 *  - `isDisplayInfo`: the `joined` message's names/timings map (sim-host/display-info.ts shape).
 *
 * Usage: `const effects = decodeEffects(a.effects); const events = decodeEvents(s.events, selfId);`
 */
import { MAX_ACTOR_EFFECTS, MAX_SNAPSHOT_EVENTS, type ActorEffect, type SnapshotEvent } from "../sim-host/snapshot.js";
import type { DisplayInfo } from "../sim-host/display-info.js";

type Rec = Record<string, unknown>;
const isRec = (v: unknown): v is Rec => typeof v === "object" && v !== null && !Array.isArray(v);
const isNum = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);
const isStr = (v: unknown): v is string => typeof v === "string" && v.length > 0;
const CAST_SHAPES = new Set(["self", "target", "circle", "cone", "line"]);

export function decodeEffects(v: unknown): ActorEffect[] {
  if (!Array.isArray(v)) return [];
  const out: ActorEffect[] = [];
  for (const e of v) if (isRec(e) && isStr(e.id) && isNum(e.ticks) && e.ticks > 0) out.push({ id: e.id, ticks: e.ticks });
  return out.slice(0, MAX_ACTOR_EFFECTS);
}

function isInterrupt(r: Rec): boolean {
  return isNum(r.tick) && isNum(r.actorId) && isNum(r.sourceId) && typeof r.school === "string";
}

function isCast(r: Rec): boolean {
  return (
    isNum(r.tick) && isNum(r.actorId) && isStr(r.abilityId) && typeof r.shape === "string" && CAST_SHAPES.has(r.shape) &&
    isRec(r.origin) && isNum(r.origin.x) && isNum(r.origin.z) && isNum(r.yaw) &&
    (r.targetId === null || isNum(r.targetId)) && Array.isArray(r.hitIds) && r.hitIds.every(isNum)
  );
}

function isCastFailed(r: Rec, selfId: number | null): boolean {
  return isNum(r.tick) && r.actorId === selfId && isStr(r.abilityId) && isStr(r.reason);
}

export function decodeEvents(v: unknown, selfId: number | null): SnapshotEvent[] {
  if (!Array.isArray(v)) return [];
  const keep = (e: unknown): boolean => {
    if (!isRec(e)) return false;
    if (e.type === "interrupt") return isInterrupt(e);
    if (e.type === "cast") return isCast(e);
    return e.type === "cast_failed" && isCastFailed(e, selfId);
  };
  return (v.filter(keep) as SnapshotEvent[]).slice(0, MAX_SNAPSHOT_EVENTS);
}

export function isDisplayInfo(v: unknown): v is DisplayInfo {
  if (!isRec(v) || !isRec(v.abilities) || !isRec(v.actorNames)) return false;
  const abilityOk = (a: unknown): boolean => isRec(a) && isStr(a.name) && isNum(a.cooldownTicks) && isNum(a.gcdTicks) && isNum(a.castTicks);
  const combosOk = v.combos === undefined || (isRec(v.combos) && Object.values(v.combos).every(isStr));
  return Object.values(v.abilities).every(abilityOk) && Object.values(v.actorNames).every(isStr) && combosOk;
}
