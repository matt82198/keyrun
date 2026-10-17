/**
 * tests/client/net/net-readability.test.ts
 *
 * ?net=1 must read like the local host (item 5 + deferred-from-#98 a/b), proven over the REAL wire
 * path: a server Room's buildSnapshot -> JSON text -> net/protocol-guard.ts parseServerFrame ->
 * net/mapping.ts toClientSnapshot.
 *  - status effects and gcdRemainingTicks survive; malformed effect entries are dropped,
 *  - `cast` events survive; `cast_failed` survives ONLY for this client's own actor,
 *  - a cast command no longer self-targets: it carries no targetId (the server resolves it with
 *    castTargetFor -- tests/server/snapshot-net-readability.test.ts),
 *  - `joined` carries the server's DisplayInfo (ability + actor names), so labels are real names.
 */
import { describe, expect, it } from "vitest";
import { Room } from "../../../server/src/room.js";
import { loadServerContent } from "../../../server/src/content-setup.js";
import type { Snapshot as ServerWireSnapshot } from "../../../server/src/snapshot.js";
import { parseServerFrame } from "../../../client/src/net/protocol-guard.js";
import { toClientSnapshot, translateHostCommand } from "../../../client/src/net/mapping.js";
import type { ServerSnapshot } from "../../../client/src/net/wire-types.js";
import type { WebSocket } from "ws";

const TANK = 1;
const fakeWs = { readyState: 3, send: () => undefined } as unknown as WebSocket;

function overTheWire(s: ServerWireSnapshot): ServerSnapshot {
  const msg = parseServerFrame(JSON.stringify({ v: 3, type: "snapshot", snapshot: s }));
  if (msg?.type !== "snapshot") throw new Error("snapshot frame did not parse");
  return msg.snapshot;
}

function humanRoom(): Room {
  const room = new Room("net-client-read", loadServerContent());
  room.attachHuman(TANK, fakeWs);
  return room;
}

describe("net mapping: status effects + gcd", () => {
  it("YOUR Brace (braceSelf) and the GCD arrive on the client snapshot exactly as the server built them", () => {
    const room = humanRoom();
    room.enqueueCommand(TANK, { tick: room.run.state.tick, kind: "cast", abilityId: "warden.brace" });
    room.tick();
    const s = room.tick().snapshot;
    const server = s.actors.find((a) => a.id === TANK)!;
    expect(server.effects.map((e) => e.id)).toContain("braceSelf");
    const client = toClientSnapshot(overTheWire(s), TANK).actors.find((a) => a.id === TANK)!;
    expect(client.effects).toEqual(server.effects);
    expect(client.gcdRemainingTicks).toBe(server.gcdRemainingTicks);
  });

  it("malformed effect entries never reach the renderer; a missing list maps to none", () => {
    const s = overTheWire(humanRoom().tick().snapshot);
    const dirty = { ...s, actors: s.actors.map((a, i) => (i === 0 ? { ...a, effects: [{ id: "stunned", ticks: 9 }, { id: 5 }, null, { id: "chill", ticks: -1 }, { id: "", ticks: 3 }] } : { ...a, effects: undefined })) } as unknown as ServerSnapshot;
    const out = toClientSnapshot(dirty, TANK);
    expect(out.actors[0]!.effects).toEqual([{ id: "stunned", ticks: 9 }]);
    expect(out.actors[1]!.effects).toEqual([]);
  });
});

describe("net mapping: cast + cast_failed events", () => {
  it("YOUR Strike's cast event survives the wire; its failed repeat arrives as cast_failed for you only", () => {
    const room = humanRoom();
    room.enqueueCommand(TANK, { tick: room.run.state.tick, kind: "cast", abilityId: "warden.strike" });
    const first = room.tick().snapshot;
    const cast = first.events.find((e) => e.type === "cast" && e.actorId === TANK);
    expect(cast).toBeDefined();
    expect(toClientSnapshot(overTheWire(first), TANK).events).toContainEqual(cast);
    room.enqueueCommand(TANK, { tick: room.run.state.tick, kind: "cast", abilityId: "warden.strike" });
    const second = room.tick().snapshot;
    const failed = second.events.find((e) => e.type === "cast_failed");
    expect(failed).toMatchObject({ actorId: TANK, reason: "Not ready yet" });
    expect(toClientSnapshot(overTheWire(second), TANK).events).toContainEqual(failed);
    expect((toClientSnapshot(overTheWire(second), 2).events ?? []).some((e) => e.type === "cast_failed")).toBe(false);
  });

  it("malformed cast / cast_failed entries are dropped", () => {
    const s = overTheWire(humanRoom().tick().snapshot);
    const good = { type: "cast_failed", tick: 1, actorId: TANK, abilityId: "warden.strike", reason: "No target" };
    const dirty = { ...s, events: [good, { type: "cast", actorId: 1 }, { type: "cast_failed", actorId: TANK }, { type: "cast", tick: 1, actorId: 1, abilityId: "x", shape: "blob", origin: { x: 0, z: 0 }, yaw: 0, targetId: null, hitIds: [] }] } as unknown as ServerSnapshot;
    expect(toClientSnapshot(dirty, TANK).events).toEqual([good]);
  });
});

describe("net mapping: cast commands", () => {
  const ctx = { selfId: 1, targetId: null, enemies: [{ id: 100, alive: true }] };
  it("a cast with nothing targeted carries no targetId (never the caster's own id)", () => {
    expect(translateHostCommand({ type: "cast", abilityId: "warden.strike", tick: 4 }, ctx)).toEqual({ tick: 4, kind: "cast", abilityId: "warden.strike" });
  });
});

describe("protocol guard: joined carries display info", () => {
  const joined = { v: 3, type: "joined", actorId: 1, role: "tank", classId: "warden", roomId: "r" };
  const display = { abilities: { "warden.strike": { name: "Strike", cooldownTicks: 0, gcdTicks: 30, castTicks: 0 } }, actorNames: { "choir.acolyte": "Choir Acolyte" } };
  it("a well-formed display map is kept; a malformed one is dropped (labels fall back, never crash)", () => {
    const ok = parseServerFrame(JSON.stringify({ ...joined, display }));
    expect(ok?.type === "joined" && ok.display).toEqual(display);
    const bad = parseServerFrame(JSON.stringify({ ...joined, display: { abilities: { x: { name: 3 } }, actorNames: {} } }));
    expect(bad?.type === "joined" && "display" in bad).toBe(false);
  });
});
