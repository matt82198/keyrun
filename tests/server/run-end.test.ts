// Usage: server-policy-in-sim (P1) deliverable 3 -- run-end detection (server/src/run-end.ts).
//
// Two scenarios:
//  1. WIPE (Room-level): `Room.tick()` is public exactly so tests can drive it directly (same
//     reason client/src/sim-host/worker.ts's `step()` is public) -- forcing a wipe through real
//     content combat would be slow and content-balance-dependent, so this kills every player actor
//     directly on `room.run.state.actors` (bypassing combat) and ticks the room WIPE_GRACE_TICKS+
//     times, watching for the "finish" SimEvent stepRunEndCheck appends to that tick's events.
//  2. ABANDON (full pipeline, through the real WebSocket): a human sends `abandon`; the room
//     broadcasts `run_end` with result "abandoned", stops itself, and lobby.ts releases it -- proven
//     by a fresh queue for the same role landing in a NEW room, not the ended one.
import { describe, expect, it } from "vitest";
import { Room } from "../../server/src/room.js";
import { buildServerContent, loadServerRegistry, DUNGEON_ID, DEFAULT_KEY_LEVEL } from "../../server/src/content-setup.js";
import { WIPE_GRACE_TICKS } from "../../server/src/run-end.js";
import type { SimEvent } from "../../sim/core/events";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

describe("server: run-end detection", () => {
  it("a full wipe auto-abandons the run after WIPE_GRACE_TICKS, exactly once", () => {
    const registry = loadServerRegistry();
    const content = buildServerContent(registry, DUNGEON_ID, DEFAULT_KEY_LEVEL);
    const room = new Room("wipe-room", content);

    for (const actor of Object.values(room.run.state.actors)) {
      if (actor.kind === "player") actor.alive = false;
    }

    const allEvents: SimEvent[] = [];
    for (let i = 0; i < WIPE_GRACE_TICKS + 5; i++) {
      allEvents.push(...room.tick().events);
    }

    const finishes = allEvents.filter((e) => e.type === "finish");
    expect(finishes).toHaveLength(1);
    expect(finishes[0]).toMatchObject({ result: "abandoned" });
  });

  it("an explicit abandon ends the run and the lobby releases the room for a fresh queue", async () => {
    let server: TestServer | undefined;
    const clients: TestClient[] = [];
    try {
      server = await startTestServer();
      const client = await TestClient.connect(server.port);
      clients.push(client);

      client.send({ v: 2, type: "queue", role: "tank" });
      const joined = await client.waitForType("joined");
      if (joined.type !== "joined") throw new Error("expected joined");
      await client.waitForType("snapshot");

      client.send({ v: 2, type: "abandon" });
      const runEnd = await client.waitForType("run_end", 8000);
      if (runEnd.type !== "run_end") throw new Error("expected run_end");
      expect(runEnd.result).toBe("abandoned");
      expect(runEnd.roomId).toBe(joined.roomId);
      expect(runEnd.rewardChoices).toHaveLength(3);

      // The ended room is released: a fresh queue for the same role lands in a NEW room.
      const client2 = await TestClient.connect(server.port);
      clients.push(client2);
      client2.send({ v: 2, type: "queue", role: "tank" });
      const joined2 = await client2.waitForType("joined");
      if (joined2.type !== "joined") throw new Error("expected joined");
      expect(joined2.roomId).not.toBe(joined.roomId);
    } finally {
      for (const c of clients) c.close();
      await server?.handle.close();
    }
  }, 15000);
});
