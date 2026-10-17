// Usage: every inbound message is untrusted -- garbage JSON, a well-formed envelope with an
// invalid command, and a wrong protocol version must all be rejected with an `error` (never crash
// the process, never silently hang the connection).
import { afterEach, describe, expect, it } from "vitest";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

describe("server: malformed input never crashes", () => {
  let server: TestServer;
  let client: TestClient;

  afterEach(async () => {
    client?.close();
    await server?.handle.close();
  });

  it("rejects non-JSON text with an error and keeps the connection usable", async () => {
    server = await startTestServer();
    client = await TestClient.connect(server.port);
    client.sendRaw("not json at all {{{");
    const err = await client.waitForType("error");
    expect(err).toMatchObject({ type: "error", reason: "malformed_message" });

    // The process and this connection are still alive: a well-formed message still works.
    client.send({ v: 3, type: "join" });
    const welcome = await client.waitForType("welcome");
    expect(welcome).toMatchObject({ type: "welcome" });
  });

  it("rejects a mismatched protocol version", async () => {
    server = await startTestServer();
    client = await TestClient.connect(server.port);
    client.sendRaw(JSON.stringify({ v: 999, type: "join" }));
    const err = await client.waitForType("error");
    expect(err).toMatchObject({ type: "error", reason: "malformed_message" });
  });

  it("rejects an invalid sim Command payload without crashing the room", async () => {
    server = await startTestServer();
    client = await TestClient.connect(server.port);
    client.send({ v: 3, type: "queue", role: "tank" });
    await client.waitForType("joined");

    client.send({ v: 3, type: "cmd", cmd: { tick: 0, kind: "teleport-to-victory" } });
    const err = await client.waitForType("error");
    expect(err).toMatchObject({ type: "error", reason: "malformed_command" });

    // The room keeps ticking (proof nothing crashed): a snapshot still arrives.
    const snap = await client.waitForType("snapshot");
    expect(snap.type).toBe("snapshot");
  });

  it("rejects a move that carries a client-supplied walk speed", async () => {
    server = await startTestServer();
    client = await TestClient.connect(server.port);
    client.send({ v: 3, type: "queue", role: "tank" });
    await client.waitForType("joined");

    client.send({ v: 3, type: "cmd", cmd: { tick: 0, kind: "move", dir: { x: 0, z: 1000 }, yaw: 0, speedMmPerTick: 9999 } });
    const err = await client.waitForType("error");
    expect(err).toMatchObject({ type: "error", reason: "malformed_command" });
  });
});
