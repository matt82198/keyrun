/**
 * client/src/net/net-client.ts
 *
 * A net host that speaks server/src/protocol.ts over WebSocket, implementing host-handle.ts's
 * `HostHandle` -- the exact interface client/src/sim-host/sim-host-client.ts's SimHostClient
 * exposes (`tickMs`, `whenReady`, `applyCommand`, `start`) -- so boot.ts/hud-wiring.ts (the render
 * side) never change: they just get handed one host or the other, picked by URL (`?net=1` /
 * `?server=`). The WebSocket handshake/reconnect lifecycle itself lives in connection.ts
 * (NetConnection); this file owns the host-interface surface, per-tick command translation
 * (mapping.ts), and turning each server snapshot into the client's own Snapshot shape.
 *
 * `whenReady()` resolves on the connection's first `joined`. Every `snapshot` after that is
 * translated (mapping.ts's `toClientSnapshot`) and handed to the `onSnapshot` callback `start()`
 * was given, identically to the local Worker host. A `late` server message and a post-reconnect
 * tick gap are surfaced via `onNotice`; connection state (connecting/connected/reconnecting/error)
 * plus this file's own `run_end` (derived from wire data -- see mapping.ts's `toClientSnapshot`,
 * or an explicit local "abandon" host control) are surfaced via `onConnectionState`.
 *
 * Usage: `const client = new NetHostClient(location.search, tickMsOverride);
 * const { humanActorId } = await client.whenReady();
 * const stop = client.start(onSnapshot, pullCommands);`
 */
import type { AnyCommand } from "../input/commands.js";
import type { Snapshot } from "../sim-host/snapshot.js";
import { SIM_TICK_MS } from "../sim-host/snapshot.js";
import type { SimHostReady } from "../sim-host/sim-host-client.js";
import { computeTicksToRun } from "../sim-host/scheduler.js";
import type { HostCommand } from "../sim-host/wire.js";
import { PROTOCOL_VERSION } from "./protocol-guard.js";
import { resolveClassId, roleForClassId, toClientSnapshot, translateHostCommand } from "./mapping.js";
import { detectBackfill } from "./reconnect.js";
import { NetConnection } from "./connection.js";
import type { ServerSnapshot } from "./wire-types.js";
import { DEFAULT_SERVER_URL, defaultWebSocketFactory, type ConnectionState, type HostHandle, type NetNotice, type WebSocketFactory } from "./host-handle.js";

export { DEFAULT_SERVER_URL };
export type { ConnectionState, HostHandle, NetNotice, WebSocketFactory };

export class NetHostClient implements HostHandle {
  readonly tickMs: number;
  private readonly conn: NetConnection;
  private readonly readyPromise: Promise<SimHostReady>;
  private readyResolve!: (r: SimHostReady) => void;
  private readyResolved = false;

  private onStateCb: ((s: ConnectionState, detail?: string) => void) | null = null;
  private onNoticeCb: ((n: NetNotice) => void) | null = null;
  private onSnapshot: ((s: Snapshot) => void) | null = null;
  private inputIntervalHandle: ReturnType<typeof setInterval> | null = null;
  private accumulatedMs = 0;
  private lastNowMs = Date.now();
  private state: ConnectionState = "connecting";
  private humanActorId: number | null = null;
  private lastSnapshotTick: number | null = null;
  private lastTargetId: number | null = null;
  private lastEnemies: { id: number; alive: boolean }[] = [];
  private runEndedAlready = false;

  constructor(search: string, tickMsOverride?: number, wsFactory: WebSocketFactory = defaultWebSocketFactory) {
    const params = new URLSearchParams(search);
    const url = params.get("server") ?? DEFAULT_SERVER_URL;
    const role = roleForClassId(resolveClassId(params.get("class")));
    this.tickMs = tickMsOverride ?? SIM_TICK_MS;
    this.readyPromise = new Promise((resolve) => {
      this.readyResolve = resolve;
    });
    this.conn = new NetConnection(url, role, wsFactory, {
      onState: (s, detail) => this.setState(s, detail),
      onJoined: (actorId) => this.handleJoined(actorId),
      onSnapshot: (s) => this.handleSnapshot(s),
      onLate: (originalTick, newTick) => this.onNoticeCb?.({ kind: "late", originalTick, newTick }),
    });
  }

  whenReady(): Promise<SimHostReady> {
    return this.readyPromise;
  }

  onConnectionState(cb: (state: ConnectionState, detail?: string) => void): void {
    this.onStateCb = cb;
    cb(this.state);
  }

  onNotice(cb: (notice: NetNotice) => void): void {
    this.onNoticeCb = cb;
  }

  applyCommand(cmd: HostCommand): void {
    const kind = (cmd as { type: string }).type;
    if (kind === "abandon") {
      if (!this.runEndedAlready) {
        this.runEndedAlready = true;
        this.setState("run_end");
      }
      return;
    }
    if (kind === "ping") return; // no sim-side wire equivalent (sim-host/wire.ts)
    const payload = translateHostCommand(cmd, { selfId: this.humanActorId ?? 0, targetId: this.lastTargetId, enemies: this.lastEnemies });
    if (payload !== null) this.conn.send({ v: PROTOCOL_VERSION, type: "cmd", cmd: payload });
  }

  /** Tick-cadenced input pump, identical shape to SimHostClient.start(). Returns a stop function. */
  start(onSnapshot: (snapshot: Snapshot) => void, pullCommands?: () => AnyCommand[]): () => void {
    this.onSnapshot = onSnapshot;
    this.lastNowMs = Date.now();
    this.inputIntervalHandle = setInterval(() => {
      const now = Date.now();
      const elapsed = now - this.lastNowMs;
      this.lastNowMs = now;
      const plan = computeTicksToRun(this.accumulatedMs, elapsed, this.tickMs);
      this.accumulatedMs = plan.nextAccumulatedMs;
      for (let i = 0; i < plan.ticks; i++) {
        if (pullCommands) for (const cmd of pullCommands()) this.applyCommand(cmd);
      }
    }, this.tickMs);
    return () => this.stop();
  }

  private handleJoined(actorId: number): void {
    this.humanActorId = actorId;
    if (!this.readyResolved) {
      this.readyResolved = true;
      this.readyResolve({ humanActorId: actorId });
    }
  }

  private handleSnapshot(s: ServerSnapshot): void {
    if (detectBackfill(this.lastSnapshotTick, s.tick)) {
      this.onNoticeCb?.({ kind: "backfilled", fromTick: this.lastSnapshotTick ?? s.tick, toTick: s.tick });
    }
    this.lastSnapshotTick = s.tick;
    this.lastEnemies = s.actors.filter((a) => a.kind === "enemy").map((a) => ({ id: a.id, alive: a.alive }));
    const self = this.humanActorId !== null ? s.actors.find((a) => a.id === this.humanActorId) : undefined;
    this.lastTargetId = self?.targetId ?? null;

    const snapshot = toClientSnapshot(s);
    if (snapshot.runEnd && !this.runEndedAlready) {
      this.runEndedAlready = true;
      this.setState("run_end");
    }
    this.onSnapshot?.(snapshot);
  }

  private setState(s: ConnectionState, detail?: string): void {
    this.state = s;
    this.onStateCb?.(s, detail);
  }

  private stop(): void {
    if (this.inputIntervalHandle !== null) clearInterval(this.inputIntervalHandle);
    this.inputIntervalHandle = null;
    this.conn.stop();
  }
}
