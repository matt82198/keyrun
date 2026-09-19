// Usage: server-policy-in-sim (P1) deliverable 2 -- a room's key level (requested via `queue`'s
// `keyLevel`) applies sim/encounter/tempo.ts's Tempo scaling exactly as client/src/sim-host/
// content-dungeon.ts does, ported into server/src/content-setup.ts's `buildServerContent`.
//
// Two proofs:
//  1. THROUGH THE WIRE: a key-2 room and a key-15 room's very first snapshot report different enemy
//     `maxHealth` for the same enemy (choir.melee, base 6000) -- the exact `scaleHealth` values.
//  2. THE SAME DATA THAT DRIVES THE WIRE: `buildServerContent`'s scaled boss EnemyScript (the boss
//     is not yet spawned at tick 0, so its cast cadence can't be observed live within a short test
//     window without also scripting a full pull clear -- see this file's own note below) has a
//     smaller loop `every` and a smaller telegraph `leadTicks` at key 15 than at key 2, matching
//     `scaleCastEvery`/`scaleLeadTicks` exactly. This is the identical `RunContent.enemyScripts`
//     object server/src/room.ts hands to `createRun` unmodified -- not a separate copy.
import { describe, expect, it } from "vitest";
import { buildServerContent, DUNGEON_ID, loadServerRegistry } from "../../server/src/content-setup.js";
import { scaleCastEvery, scaleHealth, scaleLeadTicks } from "../../sim/encounter/tempo";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

const KEY_LOW = 2;
const KEY_HIGH = 15;
const BOSS_ACTOR_ID = 900;

describe("server: key-level Tempo scaling", () => {
  it("a key-15 room's enemies report higher maxHealth than a key-2 room's, through the wire", async () => {
    let server: TestServer | undefined;
    const clients: TestClient[] = [];
    try {
      server = await startTestServer();

      const low = await TestClient.connect(server.port);
      const high = await TestClient.connect(server.port);
      clients.push(low, high);

      // Distinct roles ("tank" then "healer") so each queue is guaranteed to open its OWN new
      // room -- lobby.ts only reuses a room that still has an open slot for the requested role,
      // and a brand-new room always starts empty.
      low.send({ v: 2, type: "queue", role: "tank", keyLevel: KEY_LOW });
      const joinedLow = await low.waitForType("joined");
      if (joinedLow.type !== "joined") throw new Error("expected joined");

      high.send({ v: 2, type: "queue", role: "tank", keyLevel: KEY_HIGH });
      const joinedHigh = await high.waitForType("joined");
      if (joinedHigh.type !== "joined") throw new Error("expected joined");
      expect(joinedHigh.roomId).not.toBe(joinedLow.roomId);

      const snapLow = await low.waitForType("snapshot");
      const snapHigh = await high.waitForType("snapshot");
      if (snapLow.type !== "snapshot" || snapHigh.type !== "snapshot") throw new Error("expected snapshot");

      expect(snapLow.snapshot.keyLevel).toBe(KEY_LOW);
      expect(snapHigh.snapshot.keyLevel).toBe(KEY_HIGH);

      const enemyLow = snapLow.snapshot.actors.find((a) => a.kind === "enemy" && a.classId === "choir.melee");
      const enemyHigh = snapHigh.snapshot.actors.find((a) => a.kind === "enemy" && a.classId === "choir.melee");
      if (!enemyLow || !enemyHigh) throw new Error("expected a choir.melee enemy in the first pull");

      const baseHealth = 6000; // content/data/enemies/choir.json's choir.melee maxHealth
      expect(enemyLow.maxHealth).toBe(scaleHealth(baseHealth, KEY_LOW));
      expect(enemyHigh.maxHealth).toBe(scaleHealth(baseHealth, KEY_HIGH));
      expect(enemyHigh.maxHealth).toBeGreaterThan(enemyLow.maxHealth);
    } finally {
      for (const c of clients) c.close();
      await server?.handle.close();
    }
  });

  it("a higher key level shrinks the boss's scripted cleave loop and telegraph lead time", () => {
    const registry = loadServerRegistry();
    const low = buildServerContent(registry, DUNGEON_ID, KEY_LOW);
    const high = buildServerContent(registry, DUNGEON_ID, KEY_HIGH);

    const cleaveLow = low.content.enemyScripts[BOSS_ACTOR_ID]?.find((s) => s.cast === "hollowWarden.cleave");
    const cleaveHigh = high.content.enemyScripts[BOSS_ACTOR_ID]?.find((s) => s.cast === "hollowWarden.cleave");
    if (!cleaveLow || !cleaveHigh || cleaveLow.at !== "loop" || cleaveHigh.at !== "loop") {
      throw new Error("expected hollowWarden.cleave as a loop step on both boss scripts");
    }

    const baseEvery = 160; // content/data/enemies/hollow-warden.json
    const baseLead = 40;
    expect(cleaveLow.every).toBe(scaleCastEvery(baseEvery, KEY_LOW));
    expect(cleaveHigh.every).toBe(scaleCastEvery(baseEvery, KEY_HIGH));
    expect(cleaveHigh.every ?? 0).toBeLessThan(cleaveLow.every ?? 0);

    expect(cleaveLow.telegraph?.leadTicks).toBe(scaleLeadTicks(baseLead, KEY_LOW));
    expect(cleaveHigh.telegraph?.leadTicks).toBe(scaleLeadTicks(baseLead, KEY_HIGH));
    expect(cleaveHigh.telegraph?.leadTicks).toBeLessThanOrEqual(cleaveLow.telegraph?.leadTicks ?? 0);
  });
});
