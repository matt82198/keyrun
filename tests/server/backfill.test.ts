// Usage: a disconnecting human's actor is taken over by a bot next tick (room.ts's
// `detachHuman`), and a later human queuing for that same role reattaches to the exact same
// actorId in the exact same room -- initial fill and mid-run backfill are the same mechanism
// (room.ts's `findOpenSlot`).
import { afterEach, describe, expect, it } from "vitest";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

describe("server: disconnect -> bot takeover -> backfill", () => {
  let server: TestServer;
  let healer: TestClient;
  let tankA: TestClient | undefined;
  let tankC: TestClient | undefined;

  afterEach(async () => {
    healer?.close();
    tankA?.close();
    tankC?.close();
    await server?.handle.close();
  });

  it("hands a disconnected slot to a bot, then to the next human who queues for it", async () => {
    server = await startTestServer();

    // healer stays connected throughout, as the room's live observer.
    healer = await TestClient.connect(server.port);
    healer.send({ v: 2, type: "queue", role: "healer" });
    const joinedHealer = await healer.waitForType("joined");
    if (joinedHealer.type !== "joined") throw new Error("expected joined");

    tankA = await TestClient.connect(server.port);
    tankA.send({ v: 2, type: "queue", role: "tank" });
    const joinedA = await tankA.waitForType("joined");
    if (joinedA.type !== "joined") throw new Error("expected joined");
    expect(joinedA.roomId).toBe(joinedHealer.roomId);

    // Confirm the tank slot reads human-controlled before disconnecting it.
    const before = await healer.waitForType("snapshot");
    if (before.type !== "snapshot") throw new Error("expected snapshot");
    expect(before.snapshot.actors.find((x) => x.id === joinedA.actorId)?.controlledBy).toBe("human");

    tankA.close();
    tankA = undefined;

    // Wait for a snapshot where the tank slot has flipped back to bot-controlled.
    let flipped = false;
    for (let i = 0; i < 20 && !flipped; i++) {
      const snap = await healer.waitForType("snapshot");
      if (snap.type !== "snapshot") throw new Error("expected snapshot");
      flipped = snap.snapshot.actors.find((x) => x.id === joinedA.actorId)?.controlledBy === "bot";
    }
    expect(flipped).toBe(true);

    // A new human queuing for "tank" reattaches to the same actorId, same room.
    tankC = await TestClient.connect(server.port);
    tankC.send({ v: 2, type: "queue", role: "tank" });
    const joinedC = await tankC.waitForType("joined");
    if (joinedC.type !== "joined") throw new Error("expected joined");
    expect(joinedC.actorId).toBe(joinedA.actorId);
    expect(joinedC.roomId).toBe(joinedHealer.roomId);
  });
});
