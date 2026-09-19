/**
 * tests/client/net/net-client.test.ts
 *
 * client/src/net/net-client.ts's NetHostClient, driven headlessly with a fake WebSocket (no real
 * network) -- the join/welcome/queue/joined handshake ordering, a protocol version mismatch's
 * clear error state, a `late` server message surfaced as a notice, the server-snapshot ->
 * client-Snapshot mapping (byte-for-byte what mapping.ts's toClientSnapshot produces, so it is
 * "identical to the local host's for the same wire shapes"), and deterministic reconnect backoff
 * after an unexpected close. tests/client/net/integration.test.ts covers the real server + real
 * WebSocket path this file only fakes.
 */
import { afterEach, describe, expect, it, vi } from "vitest";
import { NetHostClient, type ConnectionState, type NetNotice } from "../../../client/src/net/net-client.js";
import type { WebSocketFactory, WebSocketLike } from "../../../client/src/net/host-handle.js";
import { toClientSnapshot } from "../../../client/src/net/mapping.js";
import type { ServerSnapshot } from "../../../client/src/net/wire-types.js";
import type { Snapshot } from "../../../client/src/sim-host/snapshot.js";

const CONNECTING = 0;
const OPEN = 1;
const CLOSED = 3;

class FakeWebSocket implements WebSocketLike {
  readyState = CONNECTING;
  sent: unknown[] = [];
  onopen: (() => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onmessage: ((ev: { data: unknown }) => void) | null = null;

  send(data: string): void {
    this.sent.push(JSON.parse(data));
  }
  close(): void {
    this.readyState = CLOSED;
    this.onclose?.();
  }
  open(): void {
    this.readyState = OPEN;
    this.onopen?.();
  }
  receive(msg: unknown): void {
    this.onmessage?.({ data: JSON.stringify(msg) });
  }
}

function makeFactory(): { factory: WebSocketFactory; sockets: FakeWebSocket[] } {
  const sockets: FakeWebSocket[] = [];
  const factory: WebSocketFactory = () => {
    const ws = new FakeWebSocket();
    sockets.push(ws);
    return ws;
  };
  return { factory, sockets };
}

function server(overrides: Partial<ServerSnapshot> = {}): ServerSnapshot {
  return {
    tick: 1,
    actors: [],
    telegraphs: [],
    runTimerTicks: 1,
    timerLimitTicks: 1000,
    slackTicks: 5,
    maxSlackTicks: 5,
    depleted: false,
    parTicks: 1000,
    pullIndex: 0,
    pullCount: 2,
    stateHash: "abc",
    ...overrides,
  };
}

async function joinAndQueue(ws: FakeWebSocket, actorId = 1): Promise<void> {
  ws.open();
  ws.receive({ v: 1, type: "welcome", protocolVersion: 1 });
  ws.receive({ v: 1, type: "joined", actorId, role: "tank", classId: "warden", roomId: "room-1" });
}

const stops: (() => void)[] = [];
afterEach(() => {
  for (const stop of stops.splice(0)) stop();
  vi.useRealTimers();
});

describe("NetHostClient: handshake ordering", () => {
  it("sends join on open, then queue only after a matching-version welcome, then resolves whenReady on joined", async () => {
    const { factory, sockets } = makeFactory();
    const client = new NetHostClient("?class=warden", 10, factory);
    const ws = sockets[0]!;
    ws.open();
    expect(ws.sent).toEqual([{ v: 1, type: "join" }]);

    ws.receive({ v: 1, type: "welcome", protocolVersion: 1 });
    expect(ws.sent).toEqual([{ v: 1, type: "join" }, { v: 1, type: "queue", role: "tank" }]);

    ws.receive({ v: 1, type: "joined", actorId: 1, role: "tank", classId: "warden", roomId: "room-1" });
    await expect(client.whenReady()).resolves.toEqual({ humanActorId: 1 });
  });

  it("maps ?class=cinder to the damage role and ?class=chanter to healer", async () => {
    const { factory: f1, sockets: s1 } = makeFactory();
    const cinder = new NetHostClient("?class=cinder", 10, f1);
    await joinAndQueue(s1[0]!);
    await cinder.whenReady();
    expect(s1[0]!.sent).toContainEqual({ v: 1, type: "queue", role: "damage" });

    const { factory: f2, sockets: s2 } = makeFactory();
    const chanter = new NetHostClient("?class=chanter", 10, f2);
    await joinAndQueue(s2[0]!);
    await chanter.whenReady();
    expect(s2[0]!.sent).toContainEqual({ v: 1, type: "queue", role: "healer" });
  });
});

describe("NetHostClient: protocol version mismatch", () => {
  it("moves to a clear error state, never queues, and whenReady never resolves", async () => {
    const { factory, sockets } = makeFactory();
    const client = new NetHostClient("", 10, factory);
    const states: { state: ConnectionState; detail: string | undefined }[] = [];
    client.onConnectionState((state, detail) => states.push({ state, detail }));

    const ws = sockets[0]!;
    ws.open();
    ws.receive({ v: 1, type: "welcome", protocolVersion: 2 });

    expect(states.at(-1)!.state).toBe("error");
    expect(states.at(-1)!.detail).toContain("protocol version mismatch");
    expect(ws.sent.some((m) => (m as { type: string }).type === "queue")).toBe(false);

    let resolved = false;
    client.whenReady().then(() => (resolved = true));
    await Promise.resolve();
    expect(resolved).toBe(false);
  });
});

describe("NetHostClient: late-command notice", () => {
  it("surfaces a server `late` message via onNotice", async () => {
    const { factory, sockets } = makeFactory();
    const client = new NetHostClient("", 10, factory);
    const notices: NetNotice[] = [];
    client.onNotice((n) => notices.push(n));

    await joinAndQueue(sockets[0]!);
    await client.whenReady();
    sockets[0]!.receive({ v: 1, type: "late", kind: "cmd", originalTick: 0, newTick: 7 });

    expect(notices).toEqual([{ kind: "late", originalTick: 0, newTick: 7 }]);
  });
});

describe("NetHostClient: snapshot -> host interface mapping", () => {
  it("hands onSnapshot exactly what mapping.ts's toClientSnapshot produces for the same wire snapshot", async () => {
    const { factory, sockets } = makeFactory();
    const client = new NetHostClient("", 10, factory);
    await joinAndQueue(sockets[0]!);
    await client.whenReady();

    const received: Snapshot[] = [];
    stops.push(client.start((s) => received.push(s)));

    const wire = server({ tick: 5 });
    sockets[0]!.receive({ v: 1, type: "snapshot", snapshot: wire });

    expect(received).toHaveLength(1);
    expect(received[0]).toEqual(toClientSnapshot(wire));
  });

  it("surfaces a `backfilled` notice when a snapshot arrives with a tick gap (a reconnect mid-run)", async () => {
    const { factory, sockets } = makeFactory();
    const client = new NetHostClient("", 10, factory);
    const notices: NetNotice[] = [];
    client.onNotice((n) => notices.push(n));
    await joinAndQueue(sockets[0]!);
    await client.whenReady();
    stops.push(client.start(() => {}));

    sockets[0]!.receive({ v: 1, type: "snapshot", snapshot: server({ tick: 5 }) });
    expect(notices).toEqual([]); // first snapshot ever: never a backfill

    sockets[0]!.receive({ v: 1, type: "snapshot", snapshot: server({ tick: 40 }) });
    expect(notices).toEqual([{ kind: "backfilled", fromTick: 5, toTick: 40 }]);
  });
});

describe("NetHostClient: reconnect", () => {
  it("retries an unexpected close with reconnect.ts's deterministic capped-exponential backoff", () => {
    vi.useFakeTimers();
    const { factory, sockets } = makeFactory();
    stops.push(new NetHostClient("", 10, factory).start(() => {}));

    sockets[0]!.open();
    sockets[0]!.close(); // never joined -- an ordinary drop, not the version-mismatch halt
    expect(sockets).toHaveLength(1);

    vi.advanceTimersByTime(249);
    expect(sockets).toHaveLength(1);
    vi.advanceTimersByTime(1); // 250ms: first backoff delay
    expect(sockets).toHaveLength(2);

    sockets[1]!.open();
    sockets[1]!.close();
    vi.advanceTimersByTime(499);
    expect(sockets).toHaveLength(2);
    vi.advanceTimersByTime(1); // 500ms: second backoff delay
    expect(sockets).toHaveLength(3);
  });

  it("resets the backoff after a successful reconnect (rejoin)", () => {
    vi.useFakeTimers();
    const { factory, sockets } = makeFactory();
    stops.push(new NetHostClient("", 10, factory).start(() => {}));

    sockets[0]!.open();
    sockets[0]!.close();
    vi.advanceTimersByTime(250);
    expect(sockets).toHaveLength(2);

    sockets[1]!.open();
    sockets[1]!.receive({ v: 1, type: "welcome", protocolVersion: 1 });
    sockets[1]!.receive({ v: 1, type: "joined", actorId: 1, role: "tank", classId: "warden", roomId: "room-1" });
    sockets[1]!.close();

    vi.advanceTimersByTime(249);
    expect(sockets).toHaveLength(2);
    vi.advanceTimersByTime(1); // back to the base delay, not the previous attempt's 500ms
    expect(sockets).toHaveLength(3);
  });
});
