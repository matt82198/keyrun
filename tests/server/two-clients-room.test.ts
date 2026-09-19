// Usage: two humans queuing for different roles land in the SAME room -- the second queue takes
// over a bot-controlled slot of the room the first queue instantly formed (lobby.ts's
// handleQueue), rather than spinning up its own bot-filled room. Proven by a command actor A
// sends showing up in actor B's own snapshot stream (shared authoritative state, not two runs).
import { afterEach, describe, expect, it } from "vitest";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

describe("server: two clients share one room", () => {
  let server: TestServer;
  let a: TestClient;
  let b: TestClient;

  afterEach(async () => {
    a?.close();
    b?.close();
    await server?.handle.close();
  });

  it("routes a second queue into the first queue's room", async () => {
    server = await startTestServer();
    a = await TestClient.connect(server.port);
    b = await TestClient.connect(server.port);

    a.send({ v: 1, type: "queue", role: "tank" });
    const joinedA = await a.waitForType("joined");
    if (joinedA.type !== "joined") throw new Error("expected joined");

    b.send({ v: 1, type: "queue", role: "healer" });
    const joinedB = await b.waitForType("joined");
    if (joinedB.type !== "joined") throw new Error("expected joined");

    expect(joinedB.roomId).toBe(joinedA.roomId);
    expect(joinedA.actorId).not.toBe(joinedB.actorId);

    // A moves; both A's and B's own snapshot streams must show A's actor (id joinedA.actorId)
    // reporting "human"-controlled -- proof they are reading the same authoritative Run, not two
    // independent bot-filled rooms.
    const snapA = await a.waitForType("snapshot");
    const snapB = await b.waitForType("snapshot");
    if (snapA.type !== "snapshot" || snapB.type !== "snapshot") throw new Error("expected snapshot");
    const aInA = snapA.snapshot.actors.find((x) => x.id === joinedA.actorId);
    const aInB = snapB.snapshot.actors.find((x) => x.id === joinedA.actorId);
    expect(aInA?.controlledBy).toBe("human");
    expect(aInB?.controlledBy).toBe("human");
    const bInA = snapA.snapshot.actors.find((x) => x.id === joinedB.actorId);
    expect(bInA?.controlledBy).toBe("human");
  });
});
