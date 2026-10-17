// Usage: STATE.md defect (g) -- "server bots use EMPTY_CLASS_KIT, so networked bot party members
// never cast". Proven through the public path: a real server over ws, one human queues as tank and
// sends nothing; the bot-controlled party members (healer + 3 damage) must complete casts of their
// OWN class abilities, visible on the `event` channel. room.ts's EMPTY_CLASS_KIT is only the
// fallback for a classId content does not define (same as client/src/sim-host/bot-commands.ts):
// bots/src/kits.ts resolves the real content kit per actor from the registry that
// content-setup.ts's loadServerRegistry injects -- the same injection the local sim-host's
// run-setup.ts does. Removing that injection is the mutant this test must catch.
import { describe, expect, it } from "vitest";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";
import { ROLE_ROSTER } from "../../server/src/roster.js";

describe("server: networked bot party members cast their class kits", () => {
  it("bot healer and damage slots complete casts of their own class abilities over ws", async () => {
    let server: TestServer | undefined;
    let client: TestClient | undefined;
    try {
      server = await startTestServer();
      client = await TestClient.connect(server.port);
      client.send({ v: 3, type: "queue", role: "tank" });
      const joined = await client.waitForType("joined");
      if (joined.type !== "joined") throw new Error("expected joined");

      const castersByClass = new Map<string, Set<number>>();
      await client.waitFor((m) => {
        if (m.type !== "event") return false;
        for (const e of m.events) {
          const ev = e as { type: string; actorId?: number; abilityId?: string };
          if (ev.type !== "cast_complete" || ev.actorId === undefined || ev.actorId === joined.actorId) continue;
          const slot = ROLE_ROSTER[ev.actorId - 1];
          if (!slot || !ev.abilityId?.startsWith(`${slot.classId}.`)) continue;
          const set = castersByClass.get(slot.classId) ?? new Set<number>();
          set.add(ev.actorId);
          castersByClass.set(slot.classId, set);
        }
        return (castersByClass.get("chanter")?.size ?? 0) >= 1 && (castersByClass.get("cinder")?.size ?? 0) >= 1;
      }, 12000);

      expect(castersByClass.get("chanter")?.size).toBeGreaterThanOrEqual(1);
      expect(castersByClass.get("cinder")?.size).toBeGreaterThanOrEqual(1);
    } finally {
      client?.close();
      await server?.handle.close();
    }
  }, 20000);
});
