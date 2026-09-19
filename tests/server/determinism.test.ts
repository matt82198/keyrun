// Usage: wire-determinism proof. Five humans occupy every ROLE_ROSTER slot (no bots ever act, so
// the command stream is fully test-controlled), each sends one `move` command scheduled comfortably
// in the future (well clear of any late re-stamping), and after N ticks the server's reported
// `stateHash` (server/src/snapshot.ts) must equal a LOCAL createRun/stepRun replay of the exact
// same command stream, run through the same fixupTargeting/applyDeathsAndWipe policy layer
// room.ts applies -- proof that what the wire reports is exactly what sim/index.ts alone would
// have produced for that seed, content, and command stream.
import { describe, expect, it } from "vitest";
import { createRun, stepRun } from "../../sim/index";
import { hashState } from "../../sim/core/state";
import type { Command } from "../../sim/core/commands";
import { ROLE_ROSTER, actorIdForSlot, buildPartySpec, type PartyRole } from "../../server/src/roster.js";
import { loadServerContent } from "../../server/src/content-setup.js";
import { fixupTargeting, applyDeathsAndWipe } from "../../server/src/targeting-policy.js";
import { SEED } from "../../server/src/room.js";
import type { Snapshot } from "../../server/src/snapshot.js";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

const SETTLE_MARGIN_TICKS = 10;

describe("server: wire determinism vs. local sim replay", () => {
  it("server stateHash after N ticks equals a local createRun/stepRun replay", async () => {
    let server: TestServer | undefined;
    const clients: TestClient[] = [];
    try {
      server = await startTestServer();
      const roles = ROLE_ROSTER.map((s) => s.role);

      const actorIds: number[] = [];
      for (const role of roles) {
        const c = await TestClient.connect(server.port);
        clients.push(c);
        c.send({ v: 1, type: "queue", role });
        const joined = await c.waitForType("joined");
        if (joined.type !== "joined") throw new Error("expected joined");
        actorIds.push(joined.actorId);
      }
      // ROLE_ROSTER's fixed slot->actorId assignment means an all-human room is exactly 1..5,
      // regardless of queue order -- the same reason the underlying Run never depends on it.
      expect(actorIds).toEqual([1, 2, 3, 4, 5]);

      const first = clients[0];
      if (!first) throw new Error("no clients");
      const baseSnap = await first.waitForType("snapshot");
      if (baseSnap.type !== "snapshot") throw new Error("expected snapshot");
      const scheduledTick = baseSnap.snapshot.tick + SETTLE_MARGIN_TICKS;

      const sentCommands: Command[] = [];
      clients.forEach((c, i) => {
        const actorId = actorIds[i];
        if (actorId === undefined) return;
        const dir = { x: ((i % 2 === 0 ? 1 : -1) * 500) | 0, z: 500 };
        c.send({ v: 1, type: "cmd", cmd: { tick: scheduledTick, kind: "move", dir, yaw: 0 } });
        sentCommands.push({ tick: scheduledTick, actorId, kind: "move", dir, yaw: 0 });
      });
      // The margin above is generous for a localhost round trip, so none of these should be late
      // (late-command.test.ts covers that path deliberately); proceed straight to settling.
      const targetTick = scheduledTick + SETTLE_MARGIN_TICKS;
      let settled: Snapshot | null = null;
      while (!settled || settled.tick < targetTick) {
        const snap = await first.waitForType("snapshot", 8000);
        if (snap.type !== "snapshot") throw new Error("expected snapshot");
        settled = snap.snapshot;
      }
      const serverHash = settled.stateHash;
      const serverTick = settled.tick;

      // Local replay: identical seed/content/party/policy layer, driven only by sentCommands.
      const content = loadServerContent();
      const party = buildPartySpec(content.registry);
      const tankIndex = ROLE_ROSTER.findIndex((s: { role: PartyRole }) => s.role === "tank");
      const tankActorId = actorIdForSlot(tankIndex);
      let run = createRun(SEED, party, content.dungeon, content.content);
      fixupTargeting(run, tankActorId);
      for (let t = 0; t < serverTick; t++) {
        const due = sentCommands.filter((c) => c.tick === t);
        const stepped = stepRun(run, due);
        run = stepped.run;
        fixupTargeting(run, tankActorId);
        run = applyDeathsAndWipe(run, stepped.events, t);
      }

      expect(hashState(run.state)).toBe(serverHash);
    } finally {
      for (const c of clients) c.close();
      await server?.handle.close();
    }
  }, 20000);
});
