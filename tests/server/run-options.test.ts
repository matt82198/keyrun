/**
 * tests/server/run-options.test.ts
 *
 * P0 regression guard, server half: server/src/protocol.ts's `queue` message previously carried
 * no `clauseId`/`loadout` at all, so a human's Clause pick and build never reached the server's
 * `createRun` call (server/src/room.ts). This drives the real wire (a real `ws` server via
 * tests/server/harness.ts, a real `ws` client via ws-client.ts) with real content/data ids --
 * PROTOCOL_VERSION bumped 2 -> 3 for the addition.
 */
import { afterEach, describe, expect, it } from "vitest";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";
import { resolveLoadoutAbilities, loadServerRegistry } from "../../server/src/content-setup.js";
import { firstQueuerOptions } from "../../server/src/room-options.js";
import { buildServerContent, DUNGEON_ID, DEFAULT_KEY_LEVEL } from "../../server/src/content-setup.js";

describe("server: run-options wiring (Clause + loadout)", () => {
  let server: TestServer;
  const clients: TestClient[] = [];

  afterEach(async () => {
    for (const c of clients) c.close();
    clients.length = 0;
    await server?.handle.close();
  });

  it("welcome reports the bumped protocol version", async () => {
    server = await startTestServer();
    const c = await TestClient.connect(server.port);
    clients.push(c);
    c.send({ v: 3, type: "join" });
    const welcome = await c.waitForType("welcome");
    expect(welcome).toEqual({ v: 3, type: "welcome", protocolVersion: 3 });
  });

  it("a real clauseId (content/data/clauses.json) on the opening queue does not error -- joined + a snapshot arrive", async () => {
    server = await startTestServer();
    const c = await TestClient.connect(server.port);
    clients.push(c);
    c.send({ v: 3, type: "queue", role: "tank", clauseId: "echo" });
    const joined = await c.waitForType("joined");
    expect(joined).toMatchObject({ type: "joined", actorId: 1, role: "tank" });
    const snapshot = await c.waitForType("snapshot");
    expect(snapshot.type).toBe("snapshot");
  });

  it("an unknown clauseId is rejected with a clean error -- never a crash; the connection stays usable", async () => {
    server = await startTestServer();
    const c = await TestClient.connect(server.port);
    clients.push(c);
    c.send({ v: 3, type: "queue", role: "tank", clauseId: "not-a-real-clause" });
    const err = await c.waitForType("error");
    expect(err).toMatchObject({ type: "error" });
    if (err.type !== "error") throw new Error("expected error");
    expect(err.reason).toMatch(/clauseId/);

    // proof the connection (and server) are still alive: a second, valid queue still works.
    c.send({ v: 3, type: "queue", role: "tank" });
    const joined = await c.waitForType("joined");
    expect(joined).toMatchObject({ type: "joined" });
  });

  it("a structurally-malformed loadout is rejected at the protocol boundary (malformed_message), not a crash", async () => {
    server = await startTestServer();
    const c = await TestClient.connect(server.port);
    clients.push(c);
    c.sendRaw(JSON.stringify({ v: 3, type: "queue", role: "tank", loadout: { talentChoices: {}, gear: [{ not: "an item" }] } }));
    const err = await c.waitForType("error");
    expect(err).toMatchObject({ type: "error", reason: "malformed_message" });
  });

  it("a real, valid loadout (warden.strike -> riposte, no gear) on the opening queue does not error", async () => {
    server = await startTestServer();
    const c = await TestClient.connect(server.port);
    clients.push(c);
    c.send({ v: 3, type: "queue", role: "tank", loadout: { talentChoices: { "warden.strike": "riposte" }, gear: [] } });
    const joined = await c.waitForType("joined");
    expect(joined).toMatchObject({ type: "joined", actorId: 1, role: "tank", classId: "warden" });
    const snapshot = await c.waitForType("snapshot");
    expect(snapshot.type).toBe("snapshot");
  });

  it("an invariant-breaking loadout (a talent fork the class doesn't have) throws through Room's constructor into a clean `error`, never a crash", async () => {
    server = await startTestServer();
    const c = await TestClient.connect(server.port);
    clients.push(c);
    c.send({ v: 3, type: "queue", role: "tank", loadout: { talentChoices: { "not-a-real-ability": "nope" }, gear: [] } });
    const err = await c.waitForType("error");
    expect(err).toMatchObject({ type: "error" });
    if (err.type !== "error") throw new Error("expected error");
    expect(err.reason).toMatch(/no talent fork/);

    // proof the connection (and server) are still alive after the throw.
    c.send({ v: 3, type: "queue", role: "tank" });
    const joined = await c.waitForType("joined");
    expect(joined).toMatchObject({ type: "joined" });
  });

  it("SAME DATA THAT DRIVES THE WIRE: resolveLoadoutAbilities patches the real warden.strike ability against the real registry", () => {
    const registry = loadServerRegistry();
    const patched = resolveLoadoutAbilities(registry, "warden", { talentChoices: { "warden.strike": "riposte" }, gear: [] });
    const base = registry.abilities.get("warden.strike");
    expect(base).toBeDefined();
    expect(patched["warden.strike"]?.gcdTicks).toBe(0); // riposte's patch: "Strike costs no GCD ..."
    expect(patched["warden.strike"]?.gcdTicks).not.toBe(base?.gcdTicks);
  });

  it("SAME DATA THAT DRIVES THE WIRE: firstQueuerOptions folds a real Clause into RunOptions.clauseId for the actor the role maps to", () => {
    const registry = loadServerRegistry();
    const sc = buildServerContent(registry, DUNGEON_ID, DEFAULT_KEY_LEVEL);
    const options = firstQueuerOptions(sc, { role: "tank", clauseId: "tide" });
    expect(options?.clauseId).toBe("tide");
    expect(options?.loadouts).toBeUndefined();

    const withLoadout = firstQueuerOptions(sc, { role: "tank", loadout: { talentChoices: { "warden.strike": "riposte" }, gear: [] } });
    expect(withLoadout?.loadouts?.[1]?.["warden.strike"]?.gcdTicks).toBe(0); // actorId 1 == tank slot (roster.ts)
  });
});
