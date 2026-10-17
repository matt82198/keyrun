/**
 * tests/client/net/mapping-parity.test.ts
 *
 * Codec round-trip for the D10 readability fields on the ?net=1 path: a REAL server Room's
 * buildSnapshot output (server/src/snapshot.ts) -> JSON wire text -> net/protocol-guard.ts's
 * parseServerFrame -> net/mapping.ts's toClientSnapshot. ActorCast.kickable, each telegraph's
 * telegraphClass, and Snapshot.events must survive byte-exact; malformed wire events never reach
 * the renderer.
 */
import { describe, expect, it } from "vitest";
import { Room } from "../../../server/src/room.js";
import { buildServerContent, loadServerContent, loadServerRegistry } from "../../../server/src/content-setup.js";
import type { Snapshot as ServerWireSnapshot } from "../../../server/src/snapshot.js";
import { parseServerFrame } from "../../../client/src/net/protocol-guard.js";
import { toClientSnapshot } from "../../../client/src/net/mapping.js";
import type { ServerSnapshot } from "../../../client/src/net/wire-types.js";
import type { ClassedTelegraphSnapshot } from "../../../client/src/sim-host/telegraph-map.js";

function overTheWire(s: ServerWireSnapshot): ServerSnapshot {
  const wire = JSON.stringify({ v: 3, type: "snapshot", snapshot: s });
  const msg = parseServerFrame(wire);
  if (msg?.type !== "snapshot") throw new Error("snapshot frame did not parse");
  expect(JSON.stringify(msg.snapshot)).toBe(JSON.stringify(s)); // byte-exact through the guard
  return msg.snapshot;
}

function firstWhere(room: Room, pred: (s: ServerWireSnapshot) => boolean, maxTicks: number): ServerWireSnapshot {
  for (let i = 0; i < maxTicks; i++) {
    const { snapshot } = room.tick();
    if (pred(snapshot)) return snapshot;
  }
  throw new Error("condition never met");
}

describe("net codec: kickable survives server -> wire -> client", () => {
  it("the Choir lieutenant's heal (interruptible) arrives kickable: true", () => {
    const room = new Room("codec-kick", loadServerContent());
    const s = firstWhere(room, (x) => x.actors.some((a) => a.id === 102 && a.cast !== null), 400);
    expect(s.actors.find((a) => a.id === 102)!.cast).toMatchObject({ abilityId: "choir.healCast", kickable: true });
    const client = toClientSnapshot(overTheWire(s));
    expect(client.actors.find((a) => a.id === 102)!.cast).toEqual(s.actors.find((a) => a.id === 102)!.cast);
  });
});

describe("net codec: telegraphClass survives server -> wire -> client", () => {
  it("The Pair's telegraphs (pull rotated first) arrive spread/stack, deep-equal to the server's", () => {
    const registry = loadServerRegistry();
    const d = registry.dungeons.get("hollow-vault")!;
    registry.dungeons.set("hollow-vault", { ...d, pulls: [d.pulls.find((p) => p.id === "pair")!, ...d.pulls.filter((p) => p.id !== "pair")] });
    const room = new Room("codec-tg", buildServerContent(registry, "hollow-vault", 2));
    const s = firstWhere(room, (x) => x.telegraphs.length > 0, 600);
    const client = toClientSnapshot(overTheWire(s));
    expect(client.telegraphs).toEqual(s.telegraphs);
    for (const t of client.telegraphs as ClassedTelegraphSnapshot[]) expect(["spread", "stack"]).toContain(t.telegraphClass);
  });
});

describe("net codec: events", () => {
  const base = (): ServerWireSnapshot => new Room("codec-ev", loadServerContent()).tick().snapshot;

  it("a quiet tick carries an empty events channel on the wire", () => {
    const s = base();
    expect(s.events).toEqual([]);
    expect(toClientSnapshot(overTheWire(s)).events).toEqual([]);
  });

  it("an interrupt event round-trips exactly; malformed entries are dropped", () => {
    const kick = { type: "interrupt" as const, tick: 9, actorId: 102, sourceId: 1, school: "holy" };
    const s = { ...base(), events: [kick] };
    expect(toClientSnapshot(overTheWire(s)).events).toEqual([kick]);
    const dirty = { ...overTheWire(s), events: [kick, { type: "interrupt", actorId: "x" }, null, { type: "other" }] } as unknown as ServerSnapshot;
    expect(toClientSnapshot(dirty).events).toEqual([kick]);
  });
});
