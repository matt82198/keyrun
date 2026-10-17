/**
 * tests/client/net/net-parity.test.ts
 *
 * D10 parity of the ?net=1 path with the local worker host, through the public path (STATE.md
 * standing rule): client/src/net/net-client.ts's NetHostClient against the REAL server/src/main.ts
 * WebSocketServer (in-process, OS-assigned port, real `ws` sockets), REAL content. Only
 * setInterval/clearInterval are faked, so the room's tick loop advances in lockstep with the test
 * (one TICK_MS advance = exactly one authoritative tick) and every scenario is deterministic.
 * Commands are stamped one tick in the past so the server answers each with a `late` notice --
 * the test waits for that notice before ticking, so a command is never raced past its tick.
 *
 * 3. Slots: `joined` carries the human's slot map, from input/slot-abilities.ts's
 *    resolveSlotAbilities (the one authority), so ability keys cast over the network.
 * 4. Readability: the wire snapshot carries ActorCast.kickable, Snapshot.events (landed kicks),
 *    and each telegraph's telegraphClass, and net/mapping.ts hands them to the renderer.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { WebSocket as NodeWebSocket } from "ws";
import { startServer, type ServerHandle } from "../../../server/src/main.js";
import { loadServerRegistry } from "../../../server/src/content-setup.js";
import { TICK_MS } from "../../../sim/core/tick.js";
import type { ContentRegistry } from "../../../content/schema/registry.js";
import { NetHostClient } from "../../../client/src/net/net-client.js";
import type { WebSocketLike } from "../../../client/src/net/host-handle.js";
import type { Snapshot } from "../../../client/src/sim-host/snapshot.js";
import type { HostCommand } from "../../../client/src/sim-host/wire.js";
import type { SimHostReady } from "../../../client/src/sim-host/sim-host-client.js";
import { DASH_CODE, deriveAbilityCommands } from "../../../client/src/input/commands.js";
import { DASH_DISTANCE_MM } from "../../../sim/combat/movement.js";
import { DEFAULT_KEYBINDS } from "../../../client/src/input/keybinds.js";
import { resolveSlotAbilities } from "../../../client/src/input/slot-abilities.js";
import { isDangerClass, type ClassedTelegraphSnapshot } from "../../../client/src/sim-host/telegraph-map.js";

const HUMAN = 1;
const LIEUTENANT = 102;
const HEAL = "choir.healCast";

async function waitUntil(pred: () => boolean, what: string, timeoutMs = 5_000): Promise<void> {
  const startedAt = Date.now();
  while (!pred()) {
    if (Date.now() - startedAt > timeoutMs) throw new Error(`net-parity: timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 2));
  }
}

interface Session {
  ready: SimHostReady;
  snaps: Snapshot[];
  last(): Snapshot;
  /** Advances the room exactly `n` ticks and waits until the client has received every snapshot. */
  step(n?: number): Promise<Snapshot>;
  /** Sends commands (stamped one tick late) and waits for the server to acknowledge each. */
  send(cmds: HostCommand[]): Promise<void>;
}

let handle: ServerHandle | undefined;
let stopClient: (() => void) | undefined;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] });
});

afterEach(async () => {
  stopClient?.();
  stopClient = undefined;
  await handle?.close();
  handle = undefined;
  vi.useRealTimers();
});

async function connect(registry: ContentRegistry = loadServerRegistry()): Promise<Session> {
  handle = await startServer(0, registry);
  const factory = (url: string): WebSocketLike => new NodeWebSocket(url) as unknown as WebSocketLike;
  const client = new NetHostClient(`?class=warden&server=ws://127.0.0.1:${handle.port}`, TICK_MS, factory);
  let lateAcks = 0;
  client.onNotice((n) => {
    if (n.kind === "late") lateAcks += 1;
  });
  const ready = await client.whenReady();
  const snaps: Snapshot[] = [];
  stopClient = client.start((s) => snaps.push(s));
  const session: Session = {
    ready,
    snaps,
    last: () => snaps.at(-1)!,
    async step(n = 1) {
      const target = snaps.length + n;
      for (let i = 0; i < n; i++) vi.advanceTimersByTime(TICK_MS);
      await waitUntil(() => snaps.length >= target, `${n} snapshot(s)`);
      return session.last();
    },
    async send(cmds) {
      const want = lateAcks + cmds.length;
      for (const c of cmds) client.applyCommand(c);
      await waitUntil(() => lateAcks >= want, "late acks");
    },
  };
  await session.step();
  return session;
}

const pastTick = (s: Session): number => s.last().tick - 1;
const actor = (s: Snapshot, id: number) => s.actors.find((a) => a.id === id)!;

describe("net parity: joined carries the slot map (gap 3)", () => {
  it("whenReady().slotAbilities is resolveSlotAbilities(warden) -- the same authority the local host uses", async () => {
    const registry = loadServerRegistry();
    const s = await connect(registry);
    expect(s.ready.slotAbilities).toEqual(resolveSlotAbilities(registry.classes.get("warden")!));
  });

  it("pressing E over the network casts the warden's E-slot ability (warden.brace) and puts it on cooldown", async () => {
    const s = await connect();
    const cmds = deriveAbilityCommands(new Set([DEFAULT_KEYBINDS.abilityE]), DEFAULT_KEYBINDS, s.ready.slotAbilities, pastTick(s));
    expect(cmds).toEqual([{ type: "cast", abilityId: "warden.brace", tick: pastTick(s) }]);
    await s.send(cmds);
    const after = await s.step(2);
    expect(actor(after, HUMAN).cooldowns["warden.brace"] ?? 0).toBeGreaterThan(0);
  });
});

describe("net parity: Space dash crosses the wire (D10)", () => {
  // +z walks the warden away from the party/boss so no collision push-out muddies the distance.
  const away = (tick: number): HostCommand => ({ type: "move", dx: 0, dz: 180, tick });
  const zOf = (s: Snapshot): number => actor(s, HUMAN).pos.z;

  it("Space with a same-tick move dashes DASH_DISTANCE_MM along it, in one tick, on top of the walk", async () => {
    const s = await connect();
    await s.send([away(pastTick(s))]);
    await s.step(); // first step clears the spawn overlap (collision push-out adds a few mm)
    const z0 = zOf(s.last());
    await s.send([away(pastTick(s))]);
    const walk = zOf(await s.step()) - z0; // control tick: walk only, in open space
    expect(walk).toBeGreaterThan(0);

    const z1 = zOf(s.last());
    const dash = deriveAbilityCommands(new Set([DASH_CODE]), DEFAULT_KEYBINDS, s.ready.slotAbilities, pastTick(s));
    expect(dash).toEqual([{ type: "dash", tick: pastTick(s) }]);
    await s.send([away(pastTick(s)), ...dash]);
    const after = await s.step();
    expect(zOf(after) - z1 - walk).toBe(DASH_DISTANCE_MM);
    expect(actor(after, HUMAN).pos.x).toBe(actor(s.snaps.at(-2)!, HUMAN).pos.x); // straight along the move
  });
});

describe("net parity: kickable and the one-shot interrupt event cross the wire (gap 4)", () => {
  it("the lieutenant's heal is kickable, and a landed kick arrives as exactly one interrupt event", async () => {
    const s = await connect();
    for (let i = 0; i < 14; i++) {
      await s.send([{ type: "move", dx: 0, dz: -1000, tick: pastTick(s) }]);
      await s.step();
    }
    for (let i = 0; i < 10 && actor(s.last(), HUMAN).targetId !== LIEUTENANT; i++) {
      await s.send([{ type: "targetCycle", tick: pastTick(s) }]);
      await s.step();
    }
    expect(actor(s.last(), HUMAN).targetId).toBe(LIEUTENANT);

    let kicked = false;
    for (let i = 0; i < 400 && !(s.last().events ?? []).some((e) => e.type === "interrupt"); i++) {
      const cast = actor(s.last(), LIEUTENANT).cast;
      if (!kicked && cast?.abilityId === HEAL) {
        expect(cast.kickable).toBe(true);
        await s.send([{ type: "cast", abilityId: "warden.shieldBash", tick: pastTick(s) }]);
        kicked = true;
      }
      await s.step();
    }
    expect(kicked).toBe(true);
    const carrying = s.snaps.filter((x) => (x.events ?? []).length > 0);
    expect(carrying).toHaveLength(1);
    const snap = carrying[0]!;
    expect(snap.events).toEqual([{ type: "interrupt", tick: snap.tick - 1, actorId: LIEUTENANT, sourceId: HUMAN, school: expect.any(String) }]);
    expect(actor(snap, LIEUTENANT).cast).toBeNull();
    await s.step(3);
    expect(s.snaps.filter((x) => (x.events ?? []).length > 0)).toHaveLength(1); // one-shot, never repeated
  });
});

describe("net parity: telegraphClass crosses the wire (gap 4)", () => {
  it("The Pair's mechanic telegraphs arrive classed spread/stack (not the avoid default), danger derived from class", async () => {
    // Real content, with Hollow Vault's pull order rotated so The Pair (spreadMark/stackCall) is
    // pull 0 -- the default-order run never reaches it within a test's budget.
    const registry = loadServerRegistry();
    const d = registry.dungeons.get("hollow-vault")!;
    registry.dungeons.set("hollow-vault", { ...d, pulls: [d.pulls.find((p) => p.id === "pair")!, ...d.pulls.filter((p) => p.id !== "pair")] });
    const s = await connect(registry);
    let tg: ClassedTelegraphSnapshot[] = [];
    for (let i = 0; i < 40 && tg.length === 0; i++) tg = (await s.step(10)).telegraphs as ClassedTelegraphSnapshot[];
    expect(tg.length).toBeGreaterThan(0);
    for (const t of tg) {
      expect(["spread", "stack"]).toContain(t.telegraphClass);
      expect(t.danger).toBe(isDangerClass(t.telegraphClass));
    }
  }, 20_000);
});
