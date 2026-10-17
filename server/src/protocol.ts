/**
 * server/src/protocol.ts
 *
 * The versioned WebSocket wire protocol: client -> server (`join`, `queue`, `cmd`, `abandon`,
 * `leave`) and server -> client (`welcome`, `queued`, `joined`, `error`, `snapshot`, `event`,
 * `late`, `run_end`, `left`). `parseClientMessage` is a total runtime guard from unknown network
 * input (raw JSON text) down to a well-formed `ClientMessage` -- never throws; returns null on
 * anything malformed so room.ts can fail closed (reject, never crash), same discipline
 * sim/core/commands.ts's `validateCommand` applies to Command payloads specifically.
 *
 * v2 (server-run-lifecycle, P1): `queue` carries an optional `keyLevel` (the room's Tempo scaling,
 * fixed at room creation -- server/CLAUDE.md/content-setup.ts); a new `abandon` message lets any
 * connected human end the run early; a new `run_end` message reports the result once the room
 * decides the run is over (server/src/run-end.ts).
 *
 * v3 (run-options-wiring): `queue` also carries an optional `clauseId` (a `content.clauses` id --
 * design/BIBLE.md sec 8's pick-of-three, same "fixed at room creation" rule `keyLevel` already
 * follows: only the queue request that actually opens a fresh room can set it) and an optional
 * `loadout` (`{talentChoices, gear}`, the screen's persisted build -- content/apply.ts's
 * `applyLoadout` input shape verbatim), applied to the actor THAT queue request will occupy (see
 * server/src/room.ts's constructor). Both are structurally validated here (a bad shape is
 * rejected as `malformed_message`, same fail-closed discipline as everything else in this file);
 * whether a `clauseId`/`loadout` is semantically VALID (a real clause, a real talent/facet/item
 * for the room's classes) is checked downstream, by `sim/index.ts`'s `createRun` (an unknown
 * clause) and `content/apply.ts`'s `applyLoadout` (an invariant-breaking loadout) -- both throw,
 * and server/src/room.ts/lobby.ts let that surface as a clean `error` message, never a crash.
 * PROTOCOL_VERSION bumped 2 -> 3 for this addition.
 *
 * Usage: `const msg = parseClientMessage(rawText); if (!msg) { ws.send(errorMsg("malformed")); return; }`
 */
import type { PartyRole } from "./roster.js";
import type { Snapshot } from "./snapshot.js";
import type { SimEvent } from "../../sim/core/events";
import type { RunResult } from "../../sim/encounter/timer";
import type { Item as RewardItem } from "../../sim/encounter/rewards";
import type { GearSlotId, Item } from "../../content/schema/facet";
import type { SlotAbilities } from "../../client/src/input/slot-abilities.js";

export const PROTOCOL_VERSION = 3;

/** The screen's persisted build (content/apply.ts's `applyLoadout` input shape verbatim) riding a
 * `queue` message -- see the v3 docstring above. */
export interface LoadoutPayload {
  talentChoices: Record<string, string>;
  gear: Item[];
}

export type ClientMessage =
  | { v: number; type: "join" }
  | { v: number; type: "queue"; role: PartyRole; keyLevel?: number; clauseId?: string; loadout?: LoadoutPayload }
  | { v: number; type: "cmd"; cmd: unknown }
  | { v: number; type: "abandon" }
  | { v: number; type: "leave" };

export type ClientMessageKind = ClientMessage["type"];

function isObject(v: unknown): v is Record<string, unknown> {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

function isRole(v: unknown): v is PartyRole {
  return v === "tank" || v === "healer" || v === "damage";
}

function isPositiveInt(v: unknown): v is number {
  return typeof v === "number" && Number.isInteger(v) && v > 0;
}

function isGearSlotId(v: unknown): v is GearSlotId {
  return v === "weapon" || v === "armor" || v === "charm" || v === "ring" || v === "keystone";
}

function isFacetRef(v: unknown): v is { id: string; text: string; abilityId: string } {
  return isObject(v) && typeof v.id === "string" && typeof v.text === "string" && typeof v.abilityId === "string";
}

function isItem(v: unknown): v is Item {
  return (
    isObject(v) &&
    typeof v.id === "string" &&
    isGearSlotId(v.slot) &&
    typeof v.tier === "number" &&
    (v.facet === null || isFacetRef(v.facet)) &&
    typeof v.charges === "number"
  );
}

/** Structural shape-guard only (see the v3 docstring above for the split with downstream semantic
 * validation): a `talentChoices` whose values are not all strings, or a `gear` entry that is not a
 * real Item shape, is rejected here as `malformed_message` -- never handed further downstream. */
function isLoadoutPayload(v: unknown): v is LoadoutPayload {
  return (
    isObject(v) &&
    isObject(v.talentChoices) &&
    Object.values(v.talentChoices).every((x) => typeof x === "string") &&
    Array.isArray(v.gear) &&
    v.gear.every(isItem)
  );
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
    case "queue": {
      if (!isRole(raw.role)) return null;
      if (raw.keyLevel !== undefined && !isPositiveInt(raw.keyLevel)) return null;
      if (raw.clauseId !== undefined && typeof raw.clauseId !== "string") return null;
      if (raw.loadout !== undefined && !isLoadoutPayload(raw.loadout)) return null;
      return {
        v: PROTOCOL_VERSION,
        type: "queue",
        role: raw.role,
        ...(raw.keyLevel !== undefined ? { keyLevel: raw.keyLevel } : {}),
        ...(raw.clauseId !== undefined ? { clauseId: raw.clauseId } : {}),
        ...(raw.loadout !== undefined ? { loadout: raw.loadout } : {}),
      };
    }
    case "cmd":
      return "cmd" in raw ? { v: PROTOCOL_VERSION, type: "cmd", cmd: raw.cmd } : null;
    case "abandon":
      return { v: PROTOCOL_VERSION, type: "abandon" };
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
  /** `slotAbilities` (D10 net parity): the joined actor's keybind slot -> ability id map, from
   * client/src/input/slot-abilities.ts's resolveSlotAbilities (the one authority both hosts use);
   * without it the net client's ability keys cast nothing (fail closed). */
  | { v: number; type: "joined"; actorId: number; role: PartyRole; classId: string; roomId: string; slotAbilities: SlotAbilities }
  | { v: number; type: "error"; reason: string }
  | { v: number; type: "snapshot"; snapshot: Snapshot }
  | { v: number; type: "event"; events: SimEvent[] }
  | { v: number; type: "late"; kind: string; originalTick: number; newTick: number }
  | { v: number; type: "run_end"; roomId: string; result: RunResult; keyLevel: number; tick: number; rewardChoices: [RewardItem, RewardItem, RewardItem] }
  | { v: number; type: "left"; actorId: number };

export function errorMessage(reason: string): ServerMessage {
  return { v: PROTOCOL_VERSION, type: "error", reason };
}
