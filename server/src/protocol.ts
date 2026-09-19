/**
 * server/src/protocol.ts
 *
 * The versioned WebSocket wire protocol: client -> server (`join`, `queue`, `cmd`, `leave`) and
 * server -> client (`welcome`, `queued`, `joined`, `error`, `snapshot`, `event`, `late`, `left`).
 * `parseClientMessage` is a total runtime guard from unknown network input (raw JSON text) down to
 * a well-formed `ClientMessage` -- never throws; returns null on anything malformed so room.ts can
 * fail closed (reject, never crash), same discipline sim/core/commands.ts's `validateCommand`
 * applies to Command payloads specifically.
 *
 * Usage: `const msg = parseClientMessage(rawText); if (!msg) { ws.send(errorMsg("malformed")); return; }`
 */
import type { PartyRole } from "./roster.js";
import type { Snapshot } from "./snapshot.js";
import type { SimEvent } from "../../sim/core/events";

export const PROTOCOL_VERSION = 1;

export type ClientMessage =
  | { v: number; type: "join" }
  | { v: number; type: "queue"; role: PartyRole }
  | { v: number; type: "cmd"; cmd: unknown }
  | { v: number; type: "leave" };

export type ClientMessageKind = ClientMessage["type"];

function isObject(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

function isRole(v: unknown): v is PartyRole {
  return v === "tank" || v === "healer" || v === "damage";
}

/** Parses one already-JSON.parsed message body into a well-typed ClientMessage, or null. Protocol
 * version must match exactly -- a mismatched `v` is rejected rather than best-effort accepted, so a
 * future protocol change is loud instead of silently misinterpreted. */
export function parseClientMessage(raw: unknown): ClientMessage | null {
  if (!isObject(raw)) return null;
  if (raw.v !== PROTOCOL_VERSION) return null;
  switch (raw.type) {
    case "join":
      return { v: PROTOCOL_VERSION, type: "join" };
    case "queue":
      return isRole(raw.role) ? { v: PROTOCOL_VERSION, type: "queue", role: raw.role } : null;
    case "cmd":
      return "cmd" in raw ? { v: PROTOCOL_VERSION, type: "cmd", cmd: raw.cmd } : null;
    case "leave":
      return { v: PROTOCOL_VERSION, type: "leave" };
    default:
      return null;
  }
}

/** Parses a raw WebSocket text frame end-to-end (JSON.parse can throw on garbage input -- this
 * never does). */
export function parseClientFrame(text: string): ClientMessage | null {
  try {
    return parseClientMessage(JSON.parse(text));
  } catch {
    return null;
  }
}

export type ServerMessage =
  | { v: number; type: "welcome"; protocolVersion: number }
  | { v: number; type: "queued"; role: PartyRole }
  | { v: number; type: "joined"; actorId: number; role: PartyRole; classId: string; roomId: string }
  | { v: number; type: "error"; reason: string }
  | { v: number; type: "snapshot"; snapshot: Snapshot }
  | { v: number; type: "event"; events: SimEvent[] }
  | { v: number; type: "late"; kind: string; originalTick: number; newTick: number }
  | { v: number; type: "left"; actorId: number };

export function errorMessage(reason: string): ServerMessage {
  return { v: PROTOCOL_VERSION, type: "error", reason };
}
