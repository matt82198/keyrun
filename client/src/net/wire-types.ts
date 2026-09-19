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

export const PROTOCOL_VERSION = 1;

export type PartyRole = "tank" | "healer" | "damage";

/** The sim/core/commands.ts Command shape, minus `actorId` (the server injects it from the
 * session, never trusts a client-supplied one -- server/CLAUDE.md). */
export type ClientCommandPayload =
  | { tick: number; kind: "move"; dir: { x: number; z: number }; yaw: number }
  | { tick: number; kind: "cast"; abilityId: string; targetId?: number }
  | { tick: number; kind: "stopcast" }
  | { tick: number; kind: "target"; targetId: number | null };

export type ClientMessage =
  | { v: number; type: "join" }
  | { v: number; type: "queue"; role: PartyRole }
  | { v: number; type: "cmd"; cmd: ClientCommandPayload }
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
  cast: { abilityId: string; startTick: number; endTick: number } | null;
  cooldowns: Record<string, number>;
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
  /** Server-only: the wire-determinism proof (server/CLAUDE.md). No client-side equivalent. */
  stateHash: string;
}

export type ServerMessage =
  | { v: number; type: "welcome"; protocolVersion: number }
  | { v: number; type: "queued"; role: PartyRole }
  | { v: number; type: "joined"; actorId: number; role: PartyRole; classId: string; roomId: string }
  | { v: number; type: "error"; reason: string }
  | { v: number; type: "snapshot"; snapshot: ServerSnapshot }
  | { v: number; type: "event"; events: unknown[] }
  | { v: number; type: "late"; kind: string; originalTick: number; newTick: number }
  | { v: number; type: "left"; actorId: number };
