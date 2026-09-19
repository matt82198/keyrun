/**
 * client/src/net/host-handle.ts
 *
 * The structural interface both client/src/sim-host/sim-host-client.ts's SimHostClient and this
 * lane's NetHostClient satisfy -- boot.ts/hud-wiring.ts (the render side) code against `HostHandle`
 * only, never a concrete class, so which host is picked (local Worker vs. this net client, chosen
 * by URL -- boot.ts) never touches render code (client/CLAUDE.md's whole premise for this lane).
 * `onConnectionState`/`onNotice` are optional: SimHostClient has no connection to report, so it
 * simply doesn't implement them; hud-wiring.ts only wires its connection-state line when present.
 *
 * Also the minimal WebSocket surface (`WebSocketLike`) net-client.ts needs -- small enough that a
 * fake (unit tests) and `ws`'s WebSocket (the integration test, and this repo's only runtime
 * WebSocket dependency) are both structurally compatible with it.
 */
import type { AnyCommand } from "../input/commands.js";
import type { Snapshot } from "../sim-host/snapshot.js";
import type { SimHostReady } from "../sim-host/sim-host-client.js";
import type { HostCommand } from "../sim-host/wire.js";

export const DEFAULT_SERVER_URL = "ws://127.0.0.1:8792";

export type ConnectionState = "connecting" | "connected" | "reconnecting" | "error" | "run_end";

export type NetNotice = { kind: "late"; originalTick: number; newTick: number } | { kind: "backfilled"; fromTick: number; toTick: number };

export interface WebSocketLike {
  readyState: number;
  send(data: string): void;
  close(): void;
  onopen: (() => void) | null;
  onclose: (() => void) | null;
  onerror: (() => void) | null;
  onmessage: ((ev: { data: unknown }) => void) | null;
}
export type WebSocketFactory = (url: string) => WebSocketLike;
export const WS_OPEN = 1;

export function defaultWebSocketFactory(url: string): WebSocketLike {
  return new WebSocket(url) as unknown as WebSocketLike;
}

export interface HostHandle {
  readonly tickMs: number;
  whenReady(): Promise<SimHostReady>;
  applyCommand(cmd: HostCommand): void;
  start(onSnapshot: (snapshot: Snapshot) => void, pullCommands?: () => AnyCommand[]): () => void;
  onConnectionState?(cb: (state: ConnectionState, detail?: string) => void): void;
  onNotice?(cb: (notice: NetNotice) => void): void;
}
