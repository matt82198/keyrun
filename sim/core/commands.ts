// Usage: the Command union and validateCommand(), a total runtime guard from unknown input
// (network/client JSON) to a well-formed Command. Never throws; returns null on anything
// malformed so callers (tick.ts) can fail closed by dropping the command rather than crashing.

import type { ActorId, Tick, Vec3 } from "./state";

export type Command = { tick: Tick; actorId: ActorId } & (
  | { kind: "move"; dir: { x: number; z: number }; yaw: number }
  | { kind: "cast"; abilityId: string; targetId?: ActorId; point?: Vec3 }
  | { kind: "stopcast" }
  | { kind: "target"; targetId: ActorId | null }
);

function isObject(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

function isInt(v: unknown): v is number {
  return typeof v === "number" && Number.isInteger(v);
}

function isIntInRange(v: unknown, min: number, max: number): v is number {
  return isInt(v) && v >= min && v <= max;
}

function isVec3(v: unknown): v is Vec3 {
  return isObject(v) && isInt(v.x) && isInt(v.y) && isInt(v.z);
}

function validateBase(c: Record<string, unknown>): { tick: Tick; actorId: ActorId } | null {
  if (!isInt(c.tick) || c.tick < 0) return null;
  if (!isInt(c.actorId)) return null;
  return { tick: c.tick, actorId: c.actorId };
}

export function validateCommand(c: unknown): Command | null {
  if (!isObject(c)) return null;
  const base = validateBase(c);
  if (!base) return null;

  switch (c.kind) {
    case "move": {
      const dir = c.dir;
      if (!isObject(dir) || !isIntInRange(dir.x, -1000, 1000) || !isIntInRange(dir.z, -1000, 1000)) return null;
      if (!isIntInRange(c.yaw, 0, 1023)) return null;
      return { ...base, kind: "move", dir: { x: dir.x, z: dir.z }, yaw: c.yaw };
    }
    case "cast": {
      if (typeof c.abilityId !== "string" || c.abilityId.length === 0) return null;
      if (c.targetId !== undefined && !isInt(c.targetId)) return null;
      if (c.point !== undefined && !isVec3(c.point)) return null;
      const targetId = c.targetId !== undefined ? (c.targetId as ActorId) : undefined;
      const point = c.point !== undefined ? (c.point as Vec3) : undefined;
      return {
        ...base,
        kind: "cast",
        abilityId: c.abilityId,
        ...(targetId !== undefined ? { targetId } : {}),
        ...(point !== undefined ? { point } : {}),
      };
    }
    case "stopcast":
      return { ...base, kind: "stopcast" };
    case "target": {
      if (!("targetId" in c)) return null;
      if (c.targetId !== null && !isInt(c.targetId)) return null;
      return { ...base, kind: "target", targetId: c.targetId as ActorId | null };
    }
    default:
      return null;
  }
}
