/**
 * tests/server/snapshot-net-readability.test.ts
 *
 * ?net=1 reads the same as the local host (items 5 + deferred-from-#98 a/b). The server snapshot
 * (server/src/snapshot.ts buildSnapshot) must carry, from the client host's own pure rules:
 *  - per-actor `effects` (snapshot-codec.ts toEffects) and `gcdRemainingTicks`,
 *  - one-shot `cast` events (cast-events.ts castEvents) next to the landed-kick events,
 * and a Room with a human must (a) resolve a cast sent WITHOUT a targetId through
 * sim-host/player-commands.ts's castTargetFor (a hostile Strike goes to the nearest live enemy,
 * never the caster) and (b) report a human press that cast nothing as `cast_failed`.
 *
 * Usage: `npx vitest run tests/server/snapshot-net-readability.test.ts`
 */
import { describe, expect, it } from "vitest";
import { createRun, stepRun, type Run } from "../../sim/index";
import type { Command } from "../../sim/core/commands";
import type { SimEvent } from "../../sim/core/events";
import { buildPartySpec } from "../../server/src/roster.js";
import { buildServerContent, DUNGEON_ID, loadServerContent, loadServerRegistry } from "../../server/src/content-setup.js";
import { Room, SEED } from "../../server/src/room.js";
import { buildSnapshot, type Snapshot } from "../../server/src/snapshot.js";
import { toEffects } from "../../client/src/sim-host/snapshot-codec.js";
import { castEvents } from "../../client/src/sim-host/cast-events.js";
import { MAX_SNAPSHOT_EVENTS } from "../../client/src/sim-host/snapshot.js";
import type { WebSocket } from "ws";
import { startTestServer, type TestServer } from "./harness.js";
import { TestClient } from "./ws-client.js";

const TANK = 1;
const LIEUTENANT = 102;

function script(tick: number): Command[] {
  const out: Command[] = [];
  if (tick === 2) out.push({ tick, actorId: TANK, kind: "cast", abilityId: "warden.brace" });
  if (tick >= 3 && tick < 17) out.push({ tick, actorId: TANK, kind: "move", dir: { x: 0, z: -1000 }, yaw: 512 });
  if (tick === 20) out.push({ tick, actorId: TANK, kind: "cast", abilityId: "warden.strike", targetId: LIEUTENANT });
  if (tick === 50) out.push({ tick, actorId: TANK, kind: "cast", abilityId: "warden.charge", targetId: LIEUTENANT });
  return out;
}

const fakeWs = { readyState: 3, send: () => undefined } as unknown as WebSocket;

describe("server snapshot: effects, gcd and cast events, from the client host's rules", () => {
  it("matches toEffects / gcdReadyTick / castEvents on every tick of a Brace-Strike-Charge script", () => {
    const content = buildServerContent(loadServerRegistry(), DUNGEON_ID, 3);
    let run: Run = createRun(SEED, buildPartySpec(content.registry), content.dungeon, content.content);
    let sawEffect = false;
    let sawCast = false;
    for (let i = 0; i < 70; i++) {
      const stepped = stepRun(run, script(run.state.tick));
      run = stepped.run;
      const snap: Snapshot = buildSnapshot(run, 0, 0, 0, new Set(), stepped.events);
      for (const a of snap.actors) {
        const actor = run.state.actors[a.id]!;
        expect(a.effects).toEqual(toEffects(actor, run.state.tick, run.content.effects));
        expect(a.gcdRemainingTicks).toBe(Math.max(0, actor.gcdReadyTick - run.state.tick));
        sawEffect ||= a.effects.length > 0;
      }
      const casts = snap.events.filter((e) => e.type === "cast");
      expect(casts).toEqual(castEvents(run, stepped.events, MAX_SNAPSHOT_EVENTS));
      sawCast ||= casts.some((e) => e.type === "cast" && e.actorId === TANK && e.abilityId === "warden.charge");
    }
    expect(sawEffect).toBe(true);
    expect(sawCast).toBe(true);
  });
});

describe("server room: human casts read like the local host", () => {
  function humanRoom(): Room {
    const room = new Room("net-read", loadServerContent());
    room.attachHuman(TANK, fakeWs);
    return room;
  }

  it("(a) a hostile Strike sent with no targetId hits the nearest live enemy, not the caster", () => {
    const room = humanRoom();
    const tick = room.run.state.tick;
    expect(room.enqueueCommand(TANK, { tick, kind: "cast", abilityId: "warden.strike" }).ok).toBe(true);
    const { snapshot, events } = room.tick();
    const hit = events.find((e: SimEvent) => e.type === "damage" && e.sourceId === TANK);
    expect(hit).toBeDefined();
    expect(room.run.state.actors[hit!.targetId as number]?.kind).toBe("enemy");
    expect(snapshot.events.some((e) => e.type === "cast" && e.actorId === TANK && e.hitIds.includes(hit!.targetId as number))).toBe(true);
  });

  it("(b) a human press that casts nothing arrives as cast_failed with the reason", () => {
    const room = humanRoom();
    room.enqueueCommand(TANK, { tick: room.run.state.tick, kind: "cast", abilityId: "warden.strike" });
    room.tick();
    room.enqueueCommand(TANK, { tick: room.run.state.tick, kind: "cast", abilityId: "warden.strike" });
    const { snapshot } = room.tick();
    expect(snapshot.events).toContainEqual({ type: "cast_failed", tick: snapshot.tick - 1, actorId: TANK, abilityId: "warden.strike", reason: "Not ready yet" });
  });

  it("bots' casts never produce cast_failed (only human presses are reported)", () => {
    const room = new Room("net-bots", loadServerContent());
    for (let i = 0; i < 200; i++) expect(room.tick().snapshot.events.some((e) => e.type === "cast_failed")).toBe(false);
  });
});

describe("server joined: display names over ws", () => {
  it("`joined` carries buildDisplayInfo's ability + actor names (labels are real names on ?net=1)", async () => {
    let server: TestServer | undefined;
    let c: TestClient | undefined;
    try {
      server = await startTestServer();
      c = await TestClient.connect(server.port);
      c.send({ v: 3, type: "queue", role: "tank" });
      const joined = await c.waitForType("joined");
      if (joined.type !== "joined") throw new Error("expected joined");
      expect(joined.display.abilities["warden.guardBreak"]?.name).toBe("Guard Break");
      expect(joined.display.abilities["warden.strike"]?.gcdTicks).toBeGreaterThan(0);
      expect(joined.display.actorNames["choir.melee"]).toBe("Choir Acolyte"); // humanizeId would say "Melee"
    } finally {
      c?.close();
      await server?.handle.close();
    }
  });
});
