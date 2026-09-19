// Usage: a command whose declared tick has already passed by the time the server processes it is
// re-stamped to the room's current tick and reported back as `late` -- never silently dropped
// (room.ts's `enqueueCommand`).
import { afterEach, describe, expect, it } from "vitest";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

describe("server: late command re-stamping", () => {
  let server: TestServer;
  let client: TestClient;

  afterEach(async () => {
    client?.close();
    await server?.handle.close();
  });

  it("re-stamps a stale-tick command to the current tick and reports `late`", async () => {
    server = await startTestServer();
    client = await TestClient.connect(server.port);
    client.send({ v: 2, type: "queue", role: "tank" });
    const joined = await client.waitForType("joined");
    if (joined.type !== "joined") throw new Error("expected joined");

    // Let a few ticks pass so tick 0 is unambiguously in the past.
    let lastTick = -1;
    for (let i = 0; i < 5; i++) {
      const snap = await client.waitForType("snapshot");
      if (snap.type !== "snapshot") throw new Error("expected snapshot");
      lastTick = snap.snapshot.tick;
    }
    expect(lastTick).toBeGreaterThan(0);

    client.send({ v: 2, type: "cmd", cmd: { tick: 0, kind: "move", dir: { x: 1, z: 0 }, yaw: 0 } });
    const late = await client.waitForType("late");
    if (late.type !== "late") throw new Error("expected late");
    expect(late.originalTick).toBe(0);
    expect(late.newTick).toBeGreaterThan(0);
    expect(late.newTick).toBeGreaterThanOrEqual(lastTick);
  });
});
