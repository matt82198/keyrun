/**
 * client/src/net/protocol-guard.ts
 *
 * Total runtime guard from unknown network input (a raw WebSocket text frame) down to a
 * well-formed ServerMessage -- never throws, same fail-closed discipline server/src/protocol.ts's
 * own parseClientMessage applies in the other direction. A malformed or unrecognized frame returns
 * null so net-client.ts can drop it silently rather than crash the connection.
 *
 * Protocol-VERSION mismatch is deliberately NOT rejected here: `welcome` still parses (its shape is
 * valid JSON), and net-client.ts's `welcome` handler is what compares `protocolVersion` against
 * this module's `PROTOCOL_VERSION` and moves the connection into a clear error state. Keeping that
 * decision out of parsing keeps this module a pure shape-guard.
 *
 * Usage: `const msg = parseServerFrame(rawText); if (!msg) return; // ignore, fail closed`
 */
import { PROTOCOL_VERSION, type ServerActorSnapshot, type ServerMessage, type ServerRunResult, type ServerSnapshot } from "./wire-types.js";
import { ABILITY_ACTIONS, type SlotAbilities } from "../input/slot-abilities.js";
import { isDisplayInfo } from "./snapshot-codec.js";

export { PROTOCOL_VERSION };

/** A complete slot map: every ability keybind action names a non-empty ability id. Anything less
 * is treated as absent (fail closed -- keys cast nothing), never partially trusted. */
function isSlotAbilities(v: unknown): v is SlotAbilities {
  return isObject(v) && ABILITY_ACTIONS.every((a) => typeof v[a] === "string" && (v[a] as string).length > 0);
}

function isObject(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

function isRole(v: unknown): v is "tank" | "healer" | "damage" {
  return v === "tank" || v === "healer" || v === "damage";
}

function isRunResult(v: unknown): v is ServerRunResult {
  return v === "timed" || v === "depleted" || v === "abandoned";
}

function isActorSnapshot(v: unknown): v is ServerActorSnapshot {
  return (
    isObject(v) &&
    typeof v.id === "number" &&
    (v.kind === "player" || v.kind === "enemy") &&
    typeof v.role === "string" &&
    typeof v.classId === "string" &&
    isObject(v.pos) &&
    typeof v.yaw === "number" &&
    typeof v.health === "number" &&
    typeof v.maxHealth === "number" &&
    typeof v.alive === "boolean" &&
    (v.controlledBy === "human" || v.controlledBy === "bot")
  );
}

function isSnapshot(v: unknown): v is ServerSnapshot {
  return (
    isObject(v) &&
    typeof v.tick === "number" &&
    Array.isArray(v.actors) &&
    v.actors.every(isActorSnapshot) &&
    Array.isArray(v.telegraphs) &&
    typeof v.runTimerTicks === "number" &&
    typeof v.timerLimitTicks === "number" &&
    typeof v.slackTicks === "number" &&
    typeof v.maxSlackTicks === "number" &&
    typeof v.depleted === "boolean" &&
    typeof v.parTicks === "number" &&
    typeof v.pullIndex === "number" &&
    typeof v.pullCount === "number" &&
    typeof v.stateHash === "string"
  );
}

/** Parses one already-JSON.parsed message body into a well-typed ServerMessage, or null. */
export function parseServerMessage(raw: unknown): ServerMessage | null {
  if (!isObject(raw) || typeof raw.v !== "number") return null;
  const v = raw.v;
  switch (raw.type) {
    case "welcome":
      return typeof raw.protocolVersion === "number" ? { v, type: "welcome", protocolVersion: raw.protocolVersion } : null;
    case "queued":
      return isRole(raw.role) ? { v, type: "queued", role: raw.role } : null;
    case "joined":
      return typeof raw.actorId === "number" && isRole(raw.role) && typeof raw.classId === "string" && typeof raw.roomId === "string"
        ? { v, type: "joined", actorId: raw.actorId, role: raw.role, classId: raw.classId, roomId: raw.roomId, ...(isSlotAbilities(raw.slotAbilities) ? { slotAbilities: raw.slotAbilities } : {}), ...(isDisplayInfo(raw.display) ? { display: raw.display } : {}) }
        : null;
    case "error":
      return typeof raw.reason === "string" ? { v, type: "error", reason: raw.reason } : null;
    case "snapshot":
      return isSnapshot(raw.snapshot) ? { v, type: "snapshot", snapshot: raw.snapshot } : null;
    case "event":
      return Array.isArray(raw.events) ? { v, type: "event", events: raw.events } : null;
    case "late":
      return typeof raw.kind === "string" && typeof raw.originalTick === "number" && typeof raw.newTick === "number"
        ? { v, type: "late", kind: raw.kind, originalTick: raw.originalTick, newTick: raw.newTick }
        : null;
    case "run_end":
      return typeof raw.roomId === "string" && isRunResult(raw.result) && typeof raw.keyLevel === "number" && typeof raw.tick === "number"
        ? { v, type: "run_end", roomId: raw.roomId, result: raw.result, keyLevel: raw.keyLevel, tick: raw.tick, rewardChoices: raw.rewardChoices }
        : null;
    case "left":
      return typeof raw.actorId === "number" ? { v, type: "left", actorId: raw.actorId } : null;
    default:
      return null;
  }
}

/** Parses a raw WebSocket text frame end-to-end (JSON.parse can throw on garbage input -- this
 * never does). */
export function parseServerFrame(text: string): ServerMessage | null {
  try {
    return parseServerMessage(JSON.parse(text));
  } catch {
    return null;
  }
}
