/**
 * tests/server/snapshot-window.test.ts
 *
 * D10 net parity for the Window ring: the server's per-actor snapshot projection
 * (server/src/snapshot.ts buildSnapshot) must carry `ActorSnapshot.window` with EXACTLY the
 * client host's semantics (client/src/sim-host/snapshot-codec.ts toWindow) -- open while
 * `endsTick > tick`, null otherwise.
 *
 * Two proofs:
 *  1. Parity on a fixture run: server content + ROLE_ROSTER party, seed SEED, the tank (warden,
 *     actor 1) walks to the Choir lieutenant (actor 102) and Strikes it (warden.strike carries
 *     `{ op: "window", windowId: "sundered", durationTicks: 60, onTarget: true }`). Every tick,
 *     every actor: server `window` deep-equals the client toWindow of the same SimState.
 *  2. Over the wire: five humans in one room (no bot ever acts), the same command script sent
 *     over ws; a snapshot during the open Window carries it, and it is null after expiry.
 *
 * Usage: `npx vitest run tests/server/snapshot-window.test.ts`
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type Run } from "../../sim/index";
import type { Command } from "../../sim/core/commands";
import { ROLE_ROSTER, buildPartySpec } from "../../server/src/roster.js";
import { buildServerContent, DUNGEON_ID, loadServerRegistry } from "../../server/src/content-setup.js";
import { SEED } from "../../server/src/room.js";
import { buildSnapshot, type Snapshot } from "../../server/src/snapshot.js";
import { toWindow } from "../../client/src/sim-host/snapshot-codec.js";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

const TANK = 1;
const LIEUTENANT = 102;
const KEY_LEVEL = 3;
const WALK_TICKS = 14;
const STRIKE_DELAY = 20;

/** The scripted tank inputs relative to a base tick: a `move` is one tick of motion, so walk
 * WALK_TICKS ticks toward the lieutenant (5000mm away, Strike range 3000mm), then Strike it. */
function script(base: number): Command[] {
  return [
    ...Array.from({ length: WALK_TICKS }, (_, i): Command => ({ tick: base + i, actorId: TANK, kind: "move", dir: { x: 0, z: -1000 }, yaw: 512 })),
    { tick: base + STRIKE_DELAY, actorId: TANK, kind: "cast", abilityId: "warden.strike", targetId: LIEUTENANT },
  ];
}

const windowOf = (s: Snapshot, id: number) => s.actors.find((a) => a.id === id)?.window;

describe("server snapshot: Window projection parity with the client host", () => {
  it("matches client toWindow for every actor on every tick, opening then expiring", () => {
    const content = buildServerContent(loadServerRegistry(), DUNGEON_ID, KEY_LEVEL);
    let run: Run = createRun(SEED, buildPartySpec(content.registry), content.dungeon, content.content);
    const cmds = script(5);
    const frames: Snapshot[] = [];
    for (let i = 0; i < 5 + STRIKE_DELAY + 90; i++) {
      const tick = run.state.tick;
      run = stepRun(run, cmds.filter((c) => c.tick === tick)).run;
      const snap = buildSnapshot(run, 0, 0, 0, new Set());
      for (const a of snap.actors) {
        const actor = run.state.actors[a.id];
        if (!actor) throw new Error(`no actor ${a.id}`);
        expect(a.window).toEqual(toWindow(actor, run.state.tick, run.content.abilities));
      }
      frames.push(snap);
    }
    const opened = frames.find((s) => windowOf(s, LIEUTENANT));
    expect(opened).toBeDefined();
    const w = windowOf(opened!, LIEUTENANT)!;
    expect(w).toMatchObject({ windowId: "sundered", setupClass: "warden", durationTicks: 60, lock: false });
    const after = frames.filter((s) => s.tick >= w.endsTick);
    expect(after.length).toBeGreaterThan(0);
    for (const s of after) expect(windowOf(s, LIEUTENANT)).toBeNull();
  });

  it("carries the open Window over ws and drops it to null after expiry", async () => {
    let server: TestServer | undefined;
    const clients: TestClient[] = [];
    try {
      server = await startTestServer();
      for (const { role } of ROLE_ROSTER) {
        const c = await TestClient.connect(server.port);
        clients.push(c);
        c.send({ v: 3, type: "queue", role, keyLevel: KEY_LEVEL });
        const joined = await c.waitForType("joined");
        if (joined.type !== "joined") throw new Error("expected joined");
      }
      const tank = clients[0]!;
      const base = await tank.waitForType("snapshot");
      if (base.type !== "snapshot") throw new Error("expected snapshot");
      for (const c of script(base.snapshot.tick + 10)) {
        const { actorId: _ignored, ...cmd } = c;
        tank.send({ v: 3, type: "cmd", cmd });
      }
      let open: Snapshot | null = null;
      let closed: Snapshot | null = null;
      while (!closed) {
        const m = await tank.waitForType("snapshot", 8000);
        if (m.type !== "snapshot") throw new Error("expected snapshot");
        const w = windowOf(m.snapshot, LIEUTENANT);
        expect(w).not.toBeUndefined();
        if (w && !open) open = m.snapshot;
        if (open && m.snapshot.tick >= windowOf(open, LIEUTENANT)!.endsTick) closed = m.snapshot;
      }
      expect(windowOf(open!, LIEUTENANT)).toMatchObject({ windowId: "sundered", setupClass: "warden", durationTicks: 60, lock: false });
      expect(windowOf(closed, LIEUTENANT)).toBeNull();
    } finally {
      for (const c of clients) c.close();
      await server?.handle.close();
    }
  }, 30000);
});
