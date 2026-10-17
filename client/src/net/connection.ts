/**
 * client/src/net/connection.ts
 *
 * Owns the WebSocket lifecycle for net-client.ts: connect -> `join`/`welcome` handshake (protocol
 * version check -- a mismatch is fatal, no retry) -> `queue` for a role -> relays `joined`/
 * `snapshot`/`late`/`error` frames to net-client.ts via `hooks`. Any unexpected close retries with
 * reconnect.ts's deterministic backoff. Split out of net-client.ts (which owns the HostHandle
 * surface and command translation) to keep both files under the repo's 170-LOC-per-file gate.
 *
 * Usage: `const conn = new NetConnection(url, role, wsFactory, hooks); conn.send(msg); conn.stop();`
 */
import { PROTOCOL_VERSION, parseServerFrame } from "./protocol-guard.js";
import { ReconnectPolicy } from "./reconnect.js";
import type { ClientMessage, PartyRole, QueueLoadoutPayload, ServerRunResult, ServerSnapshot } from "./wire-types.js";
import { WS_OPEN, type ConnectionState, type WebSocketFactory, type WebSocketLike } from "./host-handle.js";
import type { SlotAbilities } from "../input/slot-abilities.js";
import type { DisplayInfo } from "../sim-host/display-info.js";

/** run-options-wiring: the Clause pick + loadout to ride the `queue` message the instant `welcome`
 * arrives -- see net-client.ts's docstring for where these come from. */
export interface QueueExtras {
  clauseId?: string;
  loadout?: QueueLoadoutPayload;
}

export interface ConnectionHooks {
  onState(state: ConnectionState, detail?: string): void;
  /** `slotAbilities`: the server-resolved keybind slot map (absent = keys cast nothing, fail closed).
   * `display`: the server's names/timings (absent = labels fall back to humanized ids). */
  onJoined(actorId: number, slotAbilities: SlotAbilities | undefined, display?: DisplayInfo): void;
  onSnapshot(snapshot: ServerSnapshot): void;
  onLate(originalTick: number, newTick: number): void;
  /** The server's own, authoritative run-end signal (server/src/protocol.ts's `run_end` message) --
   * see net-client.ts's docstring: no client-side derivation of this exists any more. */
  onRunEnd(result: ServerRunResult, tick: number): void;
}

export class NetConnection {
  private readonly url: string;
  private readonly role: PartyRole;
  private readonly wsFactory: WebSocketFactory;
  private readonly hooks: ConnectionHooks;
  private readonly reconnectPolicy = new ReconnectPolicy();
  private ws: WebSocketLike | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private stopped = false;
  private fatal = false;
  private everConnected = false;

  constructor(url: string, role: PartyRole, wsFactory: WebSocketFactory, hooks: ConnectionHooks, private readonly queueExtras: QueueExtras = {}) {
    this.url = url;
    this.role = role;
    this.wsFactory = wsFactory;
    this.hooks = hooks;
    this.connect();
  }

  send(msg: ClientMessage): void {
    if (this.ws && this.ws.readyState === WS_OPEN) this.ws.send(JSON.stringify(msg));
  }

  stop(): void {
    this.stopped = true;
    if (this.reconnectTimer !== null) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
    this.ws?.close();
  }

  private connect(): void {
    if (this.stopped || this.fatal) return;
    this.hooks.onState(this.everConnected ? "reconnecting" : "connecting");
    const ws = this.wsFactory(this.url);
    this.ws = ws;
    ws.onopen = () => this.send({ v: PROTOCOL_VERSION, type: "join" });
    ws.onmessage = (ev) => this.handleFrame(String(ev.data));
    ws.onerror = () => {}; // onclose follows every error per the WebSocket spec; handled there.
    ws.onclose = () => this.handleClose();
  }

  private handleClose(): void {
    if (this.stopped || this.fatal) return;
    this.hooks.onState("reconnecting");
    this.reconnectTimer = setTimeout(() => this.connect(), this.reconnectPolicy.nextDelayMs());
  }

  private handleFrame(text: string): void {
    const msg = parseServerFrame(text);
    if (!msg) return; // malformed/unrecognized: fail closed, never throw
    switch (msg.type) {
      case "welcome":
        if (msg.protocolVersion !== PROTOCOL_VERSION) {
          this.fatal = true;
          this.hooks.onState("error", `protocol version mismatch: server=${msg.protocolVersion} client=${PROTOCOL_VERSION}`);
          this.ws?.close();
          return;
        }
        this.send({
          v: PROTOCOL_VERSION,
          type: "queue",
          role: this.role,
          ...(this.queueExtras.clauseId !== undefined ? { clauseId: this.queueExtras.clauseId } : {}),
          ...(this.queueExtras.loadout !== undefined ? { loadout: this.queueExtras.loadout } : {}),
        });
        return;
      case "joined":
        this.everConnected = true;
        this.reconnectPolicy.reset();
        this.hooks.onState("connected");
        this.hooks.onJoined(msg.actorId, msg.slotAbilities, msg.display);
        return;
      case "snapshot":
        this.hooks.onSnapshot(msg.snapshot);
        return;
      case "late":
        this.hooks.onLate(msg.originalTick, msg.newTick);
        return;
      case "run_end":
        this.hooks.onRunEnd(msg.result, msg.tick);
        return;
      case "error":
        this.hooks.onState("error", msg.reason);
        return;
      default:
        return; // queued/event/left: no host-interface effect
    }
  }
}
