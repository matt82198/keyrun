// Usage: baseline roundtrip -- join, queue a role, receive a `joined` ack, then a per-tick
// `snapshot` carrying the full 5-slot party (the queuer human, four bot-filled slots).
import { afterEach, describe, expect, it } from "vitest";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

describe("server: join/queue/snapshot roundtrip", () => {
  let server: TestServer;
  let client: TestClient;

  afterEach(async () => {
    client?.close();
    await server?.handle.close();
  });

  it("acks join with a versioned welcome", async () => {
    server = await startTestServer();
    client = await TestClient.connect(server.port);
    client.send({ v: 2, type: "join" });
    const welcome = await client.waitForType("welcome");
    expect(welcome).toEqual({ v: 2, type: "welcome", protocolVersion: 2 });
  });

  it("instantly forms a bot-filled room on the first queue (D8: no waiting for humans)", async () => {
    server = await startTestServer();
    client = await TestClient.connect(server.port);
    client.send({ v: 2, type: "queue", role: "tank" });

    const joined = await client.waitForType("joined");
    expect(joined).toMatchObject({ type: "joined", actorId: 1, role: "tank", classId: "warden" });

    const snapshot = await client.waitForType("snapshot");
    if (snapshot.type !== "snapshot") throw new Error("expected snapshot");
    const players = snapshot.snapshot.actors.filter((a) => a.kind === "player");
    expect(players).toHaveLength(5); // ROLE_ROSTER's fixed 5-slot party
    expect(snapshot.snapshot.actors.some((a) => a.kind === "enemy")).toBe(true); // the first pull, spawned at createRun
    const self = players.find((a) => a.id === 1);
    expect(self?.controlledBy).toBe("human");
    const others = players.filter((a) => a.id !== 1);
    expect(others.every((a) => a.controlledBy === "bot")).toBe(true);
  });

  it("opens a new room (v2: no more role_full) once every matching slot is human-controlled", async () => {
    server = await startTestServer();
    client = await TestClient.connect(server.port);
    // Fill every damage slot (3) as humans first -- kept connected, since a disconnect would
    // hand the slot back to a bot (backfill.test.ts covers that path) and defeat this test.
    const fillers: TestClient[] = [];
    let firstRoomId: string | undefined;
    for (let i = 0; i < 3; i++) {
      const c = await TestClient.connect(server.port);
      c.send({ v: 2, type: "queue", role: "damage" });
      const joined = await c.waitForType("joined");
      if (joined.type !== "joined") throw new Error("expected joined");
      firstRoomId = joined.roomId;
      fillers.push(c);
    }
    client.send({ v: 2, type: "queue", role: "damage" });
    const joined = await client.waitForType("joined");
    if (joined.type !== "joined") throw new Error("expected joined");
    expect(joined.roomId).not.toBe(firstRoomId); // routed to a fresh, second room instead of erroring
    for (const c of fillers) c.close();
  });
});
