// Usage: server-policy-in-sim (P1) deliverable 4 -- multiple concurrent rooms. Two "tank" queues
// (ROLE_ROSTER has exactly one tank slot, so the second queue can never reuse the first room) open
// two independently-ticking rooms; a move command sent into room A's tank must never show up in
// room B's snapshot stream -- proof they are two separate `Run`s, not one shared room (the
// opposite property two-clients-room.test.ts proves for a SHARED room).
import { afterEach, describe, expect, it } from "vitest";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

describe("server: multiple concurrent rooms", () => {
  let server: TestServer;
  let a: TestClient;
  let b: TestClient;

  afterEach(async () => {
    a?.close();
    b?.close();
    await server?.handle.close();
  });

  it("routes two tank queues into two separate, independently-ticking rooms", async () => {
    server = await startTestServer();
    a = await TestClient.connect(server.port);
    b = await TestClient.connect(server.port);

    a.send({ v: 3, type: "queue", role: "tank" });
    const joinedA = await a.waitForType("joined");
    if (joinedA.type !== "joined") throw new Error("expected joined");

    b.send({ v: 3, type: "queue", role: "tank" });
    const joinedB = await b.waitForType("joined");
    if (joinedB.type !== "joined") throw new Error("expected joined");

    expect(joinedB.roomId).not.toBe(joinedA.roomId);
    expect(joinedA.actorId).toBe(joinedB.actorId); // same ROLE_ROSTER slot index in each room's own Run

    const baseA = await a.waitForType("snapshot");
    if (baseA.type !== "snapshot") throw new Error("expected snapshot");
    const scheduledTick = baseA.snapshot.tick + 10;

    // Move room A's tank; room B's own tank actor must stay put -- these are two independent Runs.
    a.send({ v: 3, type: "cmd", cmd: { tick: scheduledTick, kind: "move", dir: { x: 1000, z: 0 }, yaw: 0 } });

    let movedInA = false;
    for (let i = 0; i < 30 && !movedInA; i++) {
      const snap = await a.waitForType("snapshot", 6000);
      if (snap.type !== "snapshot") throw new Error("expected snapshot");
      if (snap.snapshot.tick > scheduledTick + 3) {
        const self = snap.snapshot.actors.find((x) => x.id === joinedA.actorId);
        movedInA = self !== undefined && self.pos.x !== 0;
      }
    }
    expect(movedInA).toBe(true);

    const snapB = await b.waitForType("snapshot");
    if (snapB.type !== "snapshot") throw new Error("expected snapshot");
    const selfB = snapB.snapshot.actors.find((x) => x.id === joinedB.actorId);
    // Room B's own bots/collision resolution can nudge its tank a little off its x=0 spawn on
    // their own -- the proof is that it never travels anywhere near room A's ~1000mm+ move
    // command, not that it is pinned to exactly 0.
    expect(Math.abs(selfB?.pos.x ?? 9999)).toBeLessThan(200);
  });
});
