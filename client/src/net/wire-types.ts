/**
 * client/src/net/wire-types.ts
 *
 * Mirrors server/src/protocol.ts's ClientMessage/ServerMessage unions and server/src/snapshot.ts's
 * wire Snapshot shape (read-only reference, not imported -- server/** is off-limits to this lane;
 * same "mirrors X, not imported" convention server/src/snapshot.ts itself already uses for the
 * client -> server direction). One source of truth so protocol-guard.ts (parsing) and mapping.ts
 * (translation) don't each redeclare these unions.
 *
 * Usage: `import type { ServerMessage, ClientMessage } from "./wire-types.js";`
 */
import type { Item } from "../../../content/schema/facet.js";
import type { SlotAbilities } from "../input/slot-abilities.js";
import type { ActorCast, ActorWindow, SnapshotEvent } from "../sim-host/snapshot.js";
import type { DisplayInfo } from "../sim-host/display-info.js";
import type { TelegraphClass } from "../sim-host/telegraph-map.js";

export const PROTOCOL_VERSION = 3;

export type PartyRole = "tank" | "healer" | "damage";

/** The sim/core/commands.ts Command shape, minus `actorId` (the server injects it from the
 * session, never trusts a client-supplied one -- server/CLAUDE.md). */
export type ClientCommandPayload =
  | { tick: number; kind: "move"; dir: { x: number; z: number }; yaw: number }
  | { tick: number; kind: "dash"; dir: { x: number; z: number }; yaw: number }
  | { tick: number; kind: "cast"; abilityId: string; targetId?: number }
  | { tick: number; kind: "stopcast" }
  | { tick: number; kind: "target"; targetId: number | null };

/** run-options-wiring: mirrors server/src/protocol.ts's `LoadoutPayload` exactly (client/server
 * are separate bundles -- see that file's own "mirrors X, not imported" convention). */
export interface QueueLoadoutPayload {
  talentChoices: Record<string, string>;
  gear: Item[];
}

export type ClientMessage =
  | { v: number; type: "join" }
  | { v: number; type: "queue"; role: PartyRole; clauseId?: string; loadout?: QueueLoadoutPayload }
  | { v: number; type: "cmd"; cmd: ClientCommandPayload }
  | { v: number; type: "abandon" }
  | { v: number; type: "leave" };

export type ServerActorKind = "player" | "enemy";

export interface ServerActorSnapshot {
  id: number;
  kind: ServerActorKind;
  role: string;
  classId: string;
  pos: { x: number; y: number; z: number };
  yaw: number;
  health: number;
  maxHealth: number;
  resource: number;
  maxResource: number;
  alive: boolean;
  targetId: number | null;
  /** D10: carries `kickable` (server/src/snapshot.ts builds it with sim-host/snapshot-codec.ts's toCast). */
  cast: ActorCast | null;
  cooldowns: Record<string, number>;
  /** D10 (#74): the open Window, built by sim-host/snapshot-codec.ts's toWindow; null when none. */
  window?: ActorWindow | null;
  /** Item 5: content effects + ticks left (sim-host/snapshot-codec.ts toEffects). Untrusted until
   * net/snapshot-codec.ts's decodeEffects; optional so an older server still parses. */
  effects?: unknown;
  /** Ticks until the GCD is ready (absent on an older server -> 0). */
  gcdRemainingTicks?: number;
  /** Server-only: which side controls this actor right now. No client-side equivalent. */
  controlledBy: "human" | "bot";
}

export interface ServerTelegraphSnapshot {
  id: string;
  shape: "circle" | "cone" | "line";
  center: { x: number; z: number };
  radius?: number;
  angle1024?: number;
  yaw?: number;
  length?: number;
  width?: number;
  resolvesTick: number;
  danger: boolean;
  /** D10 render class (sim-host/telegraph-map.ts). Optional: an absent class renders as avoid. */
  telegraphClass?: TelegraphClass;
}

export interface ServerSnapshot {
  tick: number;
  actors: ServerActorSnapshot[];
  telegraphs: ServerTelegraphSnapshot[];
  runTimerTicks: number;
  timerLimitTicks: number;
  slackTicks: number;
  maxSlackTicks: number;
  depleted: boolean;
  parTicks: number;
  pullIndex: number;
  pullCount: number;
  /** D10: one-shot events -- landed kicks, completed casts, human cast_failed (server-side, the client
   * host's own rules). Untrusted until net/snapshot-codec.ts's decodeEvents. */
  events?: SnapshotEvent[];
  /** Server-only: the wire-determinism proof (server/CLAUDE.md). No client-side equivalent. */
  stateHash: string;
}

/** Mirrors sim/encounter/timer.ts's RunResult (server/src/protocol.ts, not imported -- server/**
 * is off-limits to this lane). Structurally identical to sim-host/snapshot.ts's own RunOutcome. */
export type ServerRunResult = "timed" | "depleted" | "abandoned";

export type ServerMessage =
  | { v: number; type: "welcome"; protocolVersion: number }
  | { v: number; type: "queued"; role: PartyRole }
  /** `slotAbilities`: the server's resolveSlotAbilities (input/slot-abilities.ts) for the joined
   * actor's class. Optional only so a malformed/absent map fails closed (keys cast nothing). */
  | { v: number; type: "joined"; actorId: number; role: PartyRole; classId: string; roomId: string; slotAbilities?: SlotAbilities; display?: DisplayInfo }
  | { v: number; type: "error"; reason: string }
  | { v: number; type: "snapshot"; snapshot: ServerSnapshot }
  | { v: number; type: "event"; events: unknown[] }
  | { v: number; type: "late"; kind: string; originalTick: number; newTick: number }
  /** roomId/keyLevel/rewardChoices ride along for a later lane (screen/**, off-limits here) --
   * this lane only reads `result`/`tick`. */
  | { v: number; type: "run_end"; roomId: string; result: ServerRunResult; keyLevel: number; tick: number; rewardChoices: unknown }
  | { v: number; type: "left"; actorId: number };
