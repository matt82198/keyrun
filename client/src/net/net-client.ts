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
 * tick gap are surfaced via `onNotice`.
 *
 * P2 (policy-inside-stepRun): the run's end is now the server's own, authoritative `run_end`
 * message (server/src/protocol.ts, built from sim/index.ts's stepRun via server/src/room.ts) --
 * `handleRunEnd` stores it and overlays it onto the next Snapshot handed to `onSnapshot` (or
 * immediately, against the last snapshot received, since the server stops broadcasting `snapshot`
 * the instant it sends `run_end` -- room.ts's `start()` stops its own tick loop right after). The
 * old client-side derivation (mapping.ts's `isRunEnded`/`runOutcome`, reading wipe/pull-clear
 * straight off wire snapshot data) is deleted. An explicit human abandon now genuinely reaches the
 * server too (`{type:"abandon"}`, server/src/protocol.ts) instead of only flipping this client's own
 * local connection state -- that local flip never told the server anything, so the room never
 * actually ended (a real defect this lane's ownership of net/** run_end consumption also fixes).
 *
 * Usage: `const client = new NetHostClient(location.search, tickMsOverride);
 * const { humanActorId } = await client.whenReady();
 * const stop = client.start(onSnapshot, pullCommands);`
 */
import type { AnyCommand } from "../input/commands.js";
import type { RunEndInfo, Snapshot } from "../sim-host/snapshot.js";
import { SIM_TICK_MS } from "../sim-host/snapshot.js";
import type { SimHostReady } from "../sim-host/sim-host-client.js";
import { computeTicksToRun } from "../sim-host/scheduler.js";
import type { HostCommand } from "../sim-host/wire.js";
import { PROTOCOL_VERSION } from "./protocol-guard.js";
import { resolveClassId, roleForClassId, toClientSnapshot, translateHostCommand } from "./mapping.js";
import { detectBackfill } from "./reconnect.js";
import { NetConnection } from "./connection.js";
import type { ServerRunResult, ServerSnapshot } from "./wire-types.js";
import { DEFAULT_SERVER_URL, defaultWebSocketFactory, type ConnectionState, type HostHandle, type NetNotice, type WebSocketFactory } from "./host-handle.js";
import type { RawLoadout } from "../sim-host/loadout-storage.js";
import type { SlotAbilities } from "../input/slot-abilities.js";
import type { DisplayInfo } from "../sim-host/display-info.js";
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
  private lastSelf: { targetId: number | null; yaw: number } | undefined;
  private lastMove: { tick: number; dx: number; dz: number } | null = null;
  private lastEnemies: { id: number; alive: boolean }[] = [];
  private lastSnapshot: Snapshot | null = null;
  private runEndInfo: RunEndInfo | null = null;
  private runEndedAlready = false;

  /** `loadout` (run-options-wiring): boot.ts's loadout-storage.ts read, forwarded verbatim on
   * `queue` once `welcome` arrives (connection.ts). `?clause=` rides `search` like `?class=`; this
   * lane has no registry to validate it client-side -- the server does (protocol.ts/room.ts),
   * replying `error` for a bad id rather than crashing. */
  constructor(search: string, tickMsOverride?: number, wsFactory: WebSocketFactory = defaultWebSocketFactory, loadout?: RawLoadout | null) {
    const params = new URLSearchParams(search);
    const url = params.get("server") ?? DEFAULT_SERVER_URL;
    const role = roleForClassId(resolveClassId(params.get("class")));
    const clauseId = params.get("clause");
    this.tickMs = tickMsOverride ?? SIM_TICK_MS;
    this.readyPromise = new Promise((resolve) => {
      this.readyResolve = resolve;
    });
    const hooks = {
      onState: (s: ConnectionState, detail?: string) => this.setState(s, detail),
      onJoined: (actorId: number, slotAbilities: SlotAbilities | undefined, display?: DisplayInfo) => this.handleJoined(actorId, slotAbilities, display),
      onSnapshot: (s: ServerSnapshot) => this.handleSnapshot(s),
      onLate: (originalTick: number, newTick: number) => this.onNoticeCb?.({ kind: "late", originalTick, newTick }),
      onRunEnd: (result: ServerRunResult, tick: number) => this.handleRunEnd(result, tick),
    };
    const queueExtras = { ...(clauseId?.trim() ? { clauseId } : {}), ...(loadout ? { loadout } : {}) };
    this.conn = new NetConnection(url, role, wsFactory, hooks, queueExtras);
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
      // Tells the SERVER to end the run (server/src/protocol.ts's `{type:"abandon"}`) -- the
      // authoritative `run_end` message that follows (handleRunEnd) is what actually moves this
      // client to the "run_end" connection state, not this call itself.
      this.conn.send({ v: PROTOCOL_VERSION, type: "abandon" });
      return;
    }
    if (kind === "ping") return; // no sim-side wire equivalent (sim-host/wire.ts)
    if (cmd.type === "move") this.lastMove = cmd; // a dash this same tick follows it (net/mapping.ts)
    const ctx = { selfId: this.humanActorId ?? 0, targetId: this.lastSelf?.targetId ?? null, enemies: this.lastEnemies, selfYaw: this.lastSelf?.yaw, move: this.lastMove };
    const payload = translateHostCommand(cmd, ctx);
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

  /** Resolves whenReady() with the server-sent slot map and display names (D10 net parity), so
   * input/commands.ts and the HUD read the same one-authority data the local worker host reports. */
  private handleJoined(actorId: number, slotAbilities: SlotAbilities | undefined, display?: DisplayInfo): void {
    this.humanActorId = actorId;
    if (!this.readyResolved) {
      this.readyResolved = true;
      this.readyResolve({ humanActorId: actorId, ...(slotAbilities ? { slotAbilities } : {}), ...(display ? { display } : {}) });
    }
  }

  private handleSnapshot(s: ServerSnapshot): void {
    if (detectBackfill(this.lastSnapshotTick, s.tick)) {
      this.onNoticeCb?.({ kind: "backfilled", fromTick: this.lastSnapshotTick ?? s.tick, toTick: s.tick });
    }
    this.lastSnapshotTick = s.tick;
    this.lastEnemies = s.actors.filter((a) => a.kind === "enemy").map((a) => ({ id: a.id, alive: a.alive }));
    this.lastSelf = this.humanActorId !== null ? s.actors.find((a) => a.id === this.humanActorId) : undefined;

    const snapshot = { ...toClientSnapshot(s, this.humanActorId), runEnd: this.runEndInfo };
    this.lastSnapshot = snapshot;
    this.onSnapshot?.(snapshot);
  }

  /** The server's own authoritative run-end signal (server/src/protocol.ts's `run_end` message) --
   * see file docstring. No further `snapshot` message will ever arrive once this fires (room.ts
   * stops its own tick loop right after sending it), so this re-emits the last snapshot received,
   * with `runEnd` now populated, rather than waiting for one that will never come. */
  private handleRunEnd(result: ServerRunResult, tick: number): void {
    this.runEndInfo = { outcome: result, tick };
    if (!this.runEndedAlready) {
      this.runEndedAlready = true;
      this.setState("run_end");
    }
    if (this.lastSnapshot) this.onSnapshot?.({ ...this.lastSnapshot, runEnd: this.runEndInfo });
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
