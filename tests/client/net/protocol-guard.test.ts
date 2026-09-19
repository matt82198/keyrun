// Usage: client/src/net/protocol-guard.ts's parseServerFrame/parseServerMessage never throw and
// never accept a malformed frame -- mirrors tests/server/malformed.test.ts's discipline for the
// server's own parseClientMessage, in the other direction.
import { describe, expect, it } from "vitest";
import { PROTOCOL_VERSION, parseServerFrame, parseServerMessage } from "../../../client/src/net/protocol-guard.js";

describe("protocol-guard: parseServerFrame", () => {
  it("returns null for non-JSON text instead of throwing", () => {
    expect(parseServerFrame("not json {{{")).toBeNull();
  });

  it("parses a well-formed welcome frame", () => {
    expect(parseServerFrame(JSON.stringify({ v: PROTOCOL_VERSION, type: "welcome", protocolVersion: PROTOCOL_VERSION }))).toEqual({
      v: PROTOCOL_VERSION,
      type: "welcome",
      protocolVersion: PROTOCOL_VERSION,
    });
  });
});

describe("protocol-guard: parseServerMessage", () => {
  it("rejects non-objects and objects missing a numeric v", () => {
    expect(parseServerMessage(null)).toBeNull();
    expect(parseServerMessage("welcome")).toBeNull();
    expect(parseServerMessage({ type: "welcome", protocolVersion: 1 })).toBeNull();
  });

  it("rejects an unrecognized type", () => {
    expect(parseServerMessage({ v: 2, type: "teleport-to-victory" })).toBeNull();
  });

  it.each([
    ["welcome", { protocolVersion: "not-a-number" }],
    ["queued", { role: "invalid-role" }],
    ["joined", { actorId: "1", role: "tank", classId: "warden", roomId: "room-1" }],
    ["joined", { actorId: 1, role: "tank", classId: "warden" }], // missing roomId
    ["error", { reason: 42 }],
    ["snapshot", { snapshot: { tick: 1 } }], // missing every other required field
    ["event", { events: "not-an-array" }],
    ["late", { kind: "cmd", originalTick: 0 }], // missing newTick
    ["left", { actorId: "1" }],
  ])("rejects a malformed %s payload", (type, payload) => {
    expect(parseServerMessage({ v: 2, type, ...payload })).toBeNull();
  });

  it("parses a well-formed snapshot, keeping the full shape", () => {
    const actor = {
      id: 1,
      kind: "player",
      role: "tank",
      classId: "warden",
      pos: { x: 0, y: 0, z: 0 },
      yaw: 0,
      health: 100,
      maxHealth: 100,
      resource: 0,
      maxResource: 100,
      alive: true,
      targetId: null,
      cast: null,
      cooldowns: {},
      controlledBy: "human",
    };
    const snapshot = {
      tick: 5,
      actors: [actor],
      telegraphs: [],
      runTimerTicks: 5,
      timerLimitTicks: 1000,
      slackTicks: 5,
      maxSlackTicks: 5,
      depleted: false,
      parTicks: 1000,
      pullIndex: 0,
      pullCount: 2,
      stateHash: "abc123",
    };
    const msg = parseServerMessage({ v: 2, type: "snapshot", snapshot });
    expect(msg).toEqual({ v: 2, type: "snapshot", snapshot });
  });

  it("rejects a snapshot whose actor array contains a malformed entry", () => {
    const snapshot = {
      tick: 5,
      actors: [{ id: 1 }], // missing every other actor field
      telegraphs: [],
      runTimerTicks: 5,
      timerLimitTicks: 1000,
      slackTicks: 5,
      maxSlackTicks: 5,
      depleted: false,
      parTicks: 1000,
      pullIndex: 0,
      pullCount: 2,
      stateHash: "abc123",
    };
    expect(parseServerMessage({ v: 2, type: "snapshot", snapshot })).toBeNull();
  });
});
